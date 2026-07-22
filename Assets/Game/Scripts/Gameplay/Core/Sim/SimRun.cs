using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
using R3;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Drives ONE level attempt (RFC §5.1) over the real domain graph, built by bare constructor injection
    /// (no VContainer) with an injected <see cref="IShapeFillSimulation"/> — this keeps production DI untouched
    /// AND proves the headless-simulatable invariant. It is steppable so the host can drive it either in one
    /// frame (max acceleration) or in per-frame batches (the M0 accel-safe check); the static <see cref="Run"/>
    /// is the one-frame convenience. Every step uses dt == FixedStep (ADR-0001: never enlarge the step). The
    /// caller owns the simulation lifetime — a fresh instance plus a frame yield between runs (RFC §6).
    /// </summary>
    public sealed class SimRun : IDisposable
    {
        private readonly GameModel _model;
        private readonly ISimBotPolicy _policy;
        private readonly GameController _controller;
        private readonly GameplayClock _clock;
        private readonly SimBotInputSource _input;
        private readonly ShapeFillMechanicController _mechanic;
        private readonly PoseMatchEvaluator _match;
        private readonly LevelData _level;
        private readonly IDisposable _outcomeSub;

        private GameOutcome _outcome = GameOutcome.None;

        public SimRun(LevelData level, IShapeFillSimulation simulation, ISimBotPolicy policy)
        {
            _level = level;
            _policy = policy;

            var tuning = new ShapeFillTuning();
            _model = new GameModel();
            _match = new PoseMatchEvaluator();
            _clock = new GameplayClock(tuning.FixedStep);
            _input = new SimBotInputSource();

            _mechanic = new ShapeFillMechanicController(
                _model, simulation, _match, new LevelDataValidator(), new NullPieceViewFactory(), tuning);
            _controller = new GameController(
                new FixedLevelSource(level), _model, _mechanic, simulation, _clock, _input);

            _outcomeSub = _model.Outcome.Subscribe(o => _outcome = o);
            _controller.Start(); // mechanic reacts to LevelLoaded → pre-places every piece Idle
        }

        public bool Resolved => _outcome != GameOutcome.None;

        public long PoseHash() => SimPoseHash.Of(_model);

        /// <summary>
        /// One driver iteration: if the field is quiet (no Falling piece), ask the policy to release the next
        /// piece, then advance exactly one fixed physics step + mechanic tick.
        /// </summary>
        public void Step()
        {
            if (_model.CurrentPhase == GamePhase.Running && IsFieldQuiet() &&
                _policy.TryNextRelease(_model, out var tap))
                _input.Push(tap);

            _controller.Tick(_clock.FixedStep);
        }

        public SimRunResult BuildResult(string policyId, string orderKey, int repeatIndex, int stepsToResolve)
        {
            var placed = new List<PlacedPiece>();
            var lost = 0;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost)
                    lost++;
                else if (piece.State == PieceLifecycle.Placed)
                    placed.Add(new PlacedPiece(piece.Kind, piece.Position, piece.Rotation,
                        _model.TypeFor(piece).SymmetryOrder));
            }

            var match = _match.Evaluate(_level.Target, placed, _level.MatchTolerance);
            var solved = _outcome == GameOutcome.Won;
            var outcome = _outcome == GameOutcome.None ? "Timeout" : _outcome.ToString();

            return new SimRunResult(_level.LevelId, _level.Seed, policyId, orderKey, repeatIndex,
                solved, outcome, _model.AttemptsUsed, stepsToResolve, match.TotalError,
                match.SlotsFilled, match.SlotsTotal, lost, SimPoseHash.Of(_model));
        }

        private bool IsFieldQuiet()
        {
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Falling)
                    return false;
            }
            return true;
        }

        public void Dispose()
        {
            _outcomeSub?.Dispose();
            _mechanic.Dispose();
            _controller.Dispose();
            _input.Dispose();
        }

        /// <summary>
        /// One-frame convenience run (max acceleration): steps to resolution or timeout, builds the result,
        /// then resets the simulation. The caller must still yield a frame before the next run so the deferred
        /// body <c>Destroy</c> flushes (RFC §6).
        /// </summary>
        public static SimRunResult Run(LevelData level, IShapeFillSimulation simulation, ISimBotPolicy policy,
            string policyId, string orderKey, int repeatIndex, int maxSteps)
        {
            using var run = new SimRun(level, simulation, policy);

            var steps = 0;
            for (; steps < maxSteps && !run.Resolved; steps++)
                run.Step();

            var result = run.BuildResult(policyId, orderKey, repeatIndex, steps);
            simulation.Reset();
            return result;
        }
    }
}
