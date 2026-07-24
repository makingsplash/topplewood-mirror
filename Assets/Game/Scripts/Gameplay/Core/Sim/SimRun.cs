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
        // Reused per Step so the driver allocates nothing while pushing a release group (TASK-023 §5.4).
        private readonly List<Vec2> _tapScratch = new();

        // Pre-glide residual metric (TASK-024 §5): the FIRST tick a piece flips to Placed its model pose is the
        // physical rest pose captured BEFORE the soft-settle glide moves it, so snapshotting it there measures how
        // far real contact torque actually righted the piece — not what the kinematic glide then faked. We track the
        // worst (max) angular + positional residual to the nearest same-Kind slot over the run: a small angular
        // value proves the torque did the turn; a positional value within SnapCaptureRadius proves the topple did
        // not carry the piece out of its capture window (no Lost regress). Sim-only; the production path is untouched.
        private readonly HashSet<int> _preGlideMeasured = new();
        private float _maxPreGlideAngResidual;
        private float _maxPreGlidePosErr;

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
        /// One driver iteration: when the controller reports the field is ready for the next release group
        /// (balance-aware — beam settled AND the coalescence window closed; assembly ≡ "no Falling piece"), ask
        /// the policy for the next group and push ALL its taps in this SAME step so a synchronous pair enters the
        /// physics together (TASK-023 §5.4). Then advance exactly one fixed physics step + mechanic tick.
        /// </summary>
        public void Step()
        {
            if (_model.CurrentPhase == GamePhase.Running && _mechanic.ReadyForNextRelease &&
                _policy.TryNextRelease(_model, _tapScratch))
            {
                foreach (var tap in _tapScratch)
                    _input.Push(tap);
            }

            _controller.Tick(_clock.FixedStep);
            MeasurePreGlideResiduals();
        }

        /// <summary>
        /// Snapshot each piece's residual to its nearest same-Kind slot the FIRST tick it becomes Placed (TASK-024
        /// §5). At that tick the model pose is the pre-glide physical rest pose (BeginSettle freezes and StartRot=
        /// the rest pose; AdvanceSettling only moves it from the NEXT tick), so this measures the true post-torque
        /// residual. Keeps the running max of angular (symmetry-reduced) and positional residual for the run.
        /// </summary>
        private void MeasurePreGlideResiduals()
        {
            var slots = _level.Target?.Slots;
            if (slots == null || slots.Count == 0)
                return;

            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                if (!_preGlideMeasured.Add(piece.InstanceId))
                    continue; // already snapshotted on the tick it first turned Placed

                var symmetryOrder = _model.TypeFor(piece).SymmetryOrder;
                var bestPosErr = float.MaxValue;
                var bestAng = 0f;
                for (var s = 0; s < slots.Count; s++)
                {
                    var slot = slots[s];
                    if (slot.Kind != piece.Kind)
                        continue;

                    var posErr = (slot.Position - piece.Position).Length;
                    if (posErr < bestPosErr)
                    {
                        bestPosErr = posErr;
                        bestAng = SymmetryReduce(piece.Rotation - slot.Rotation, symmetryOrder);
                    }
                }

                if (bestPosErr == float.MaxValue)
                    continue; // no same-Kind slot (should not happen for a matched level)

                if (bestAng > _maxPreGlideAngResidual)
                    _maxPreGlideAngResidual = bestAng;
                if (bestPosErr > _maxPreGlidePosErr)
                    _maxPreGlidePosErr = bestPosErr;
            }
        }

        /// <summary>Symmetry-reduced magnitude of an angle delta — same reduction as the controller/evaluator so
        /// the sim metric agrees with the snap window and the win check.</summary>
        private static float SymmetryReduce(float delta, int symmetryOrder)
        {
            var order = symmetryOrder < 1 ? 1 : symmetryOrder;
            var period = (2f * (float)Math.PI) / order;
            var d = delta % period;
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return d < 0f ? -d : d;
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
                match.SlotsFilled, match.SlotsTotal, lost, SimPoseHash.Of(_model),
                _maxPreGlideAngResidual, _maxPreGlidePosErr);
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
