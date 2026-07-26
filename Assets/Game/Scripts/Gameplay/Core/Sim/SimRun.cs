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
        // Shared with the controller so the honesty detector's interpenetration epsilon is the SAME constant as the
        // gate's (owner: "порог guard и порог детектора должны быть ОДНОЙ константой", TASK-042).
        private readonly ShapeFillTuning _tuning;
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
        // TASK-026 STEP-1 (observability): every piece's raw pose (kind + world x/y + rotation) at the SAME capture
        // tick the scalars above are read — the first tick it flips Placed, the pre-glide physical rest pose before
        // the soft-settle glide moves it. Where the scalars answer "how far off", this answers "off in which
        // direction" (the rest − slot VECTOR the owner needs to precompensate starts). Sim-only; production untouched.
        private readonly List<SimPiecePose> _preGlidePoses = new();

        private GameOutcome _outcome = GameOutcome.None;

        public SimRun(LevelData level, IShapeFillSimulation simulation, ISimBotPolicy policy)
        {
            _level = level;
            _policy = policy;

            _tuning = new ShapeFillTuning();
            _model = new GameModel();
            _match = new PoseMatchEvaluator();
            _clock = new GameplayClock(_tuning.FixedStep);
            _input = new SimBotInputSource();

            _mechanic = new ShapeFillMechanicController(
                _model, simulation, _match, new LevelDataValidator(), new NullPieceViewFactory(), _tuning);
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

                // TASK-026 STEP-1: record the raw pre-glide pose at this same capture tick (before the glide moves
                // it). State is Placed by definition here; kept for shape-parity with FinalPoses.
                _preGlidePoses.Add(new SimPiecePose(
                    piece.Kind, piece.Position.X, piece.Position.Y, piece.Rotation, piece.State.ToString()));

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
            // TASK-025 STEP-1: record EVERY piece's final pose (any state), so the balancer can author slots to the
            // deterministic rest points physics produces (05 §9) rather than blind-fit trajectories.
            var finalPoses = new List<SimPiecePose>();
            var lost = 0;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost)
                    lost++;
                else if (piece.State == PieceLifecycle.Placed)
                    placed.Add(new PlacedPiece(piece.Kind, piece.Position, piece.Rotation,
                        _model.TypeFor(piece).SymmetryOrder));

                finalPoses.Add(new SimPiecePose(
                    piece.Kind, piece.Position.X, piece.Position.Y, piece.Rotation, piece.State.ToString()));
            }

            var match = _match.Evaluate(_level.Target, placed, _level.MatchTolerance);
            var solved = _outcome == GameOutcome.Won;
            var outcome = _outcome == GameOutcome.None ? "Timeout" : _outcome.ToString();

            ComputeHonestyMetrics(out var intersectingPairs, out var maxPenetration, out var unsupportedPlaced,
                out var worst);

            return new SimRunResult(_level.LevelId, _level.Seed, policyId, orderKey, repeatIndex,
                solved, outcome, _model.AttemptsUsed, stepsToResolve, match.TotalError,
                match.SlotsFilled, match.SlotsTotal, lost, SimPoseHash.Of(_model),
                _maxPreGlideAngResidual, _maxPreGlidePosErr, finalPoses, _preGlidePoses,
                intersectingPairs, maxPenetration, unsupportedPlaced,
                _mechanic.SnapRejectsByOverlap, _mechanic.SnapRejectsBySupport, worst);
        }

        /// <summary>
        /// TASK-042 honesty detector (revised after the owner gate-FAIL 2026-07-26) — the owner's proof that physics
        /// stays honest in EVERY release order. Over the FINAL settled (Placed) poses it measures:
        /// (a) INTERPENETRATION — pairs of pieces whose convex SAT penetration exceeds their AUTHORED baseline
        ///     overlap (the same per-pair design overlap the controller gate uses, via <c>OverlapAllowance</c>) by
        ///     more than the shared <c>OverlapTolerance</c> epsilon. Measuring the OVERSHOOT above baseline — not raw
        ///     penetration — is what stops authored micro-overlaps (owl's ear in the head, birthday_cake's touching
        ///     tiers) from being false-flagged, and keeps guard-threshold == detector-threshold (owner's demand).
        ///     <c>maxPenetration</c> is the worst such overshoot (0 ⇒ every contact is within design + slop).
        /// (b) FREEZE-IN-AIR — Placed pieces not TRANSITIVELY grounded: a piece is grounded if, dropped by
        ///     <c>SupportProbe</c>, it touches a static OR an already-grounded piece (iterated to a fixed point). This
        ///     credits a load resting on a free beam that rests on a fulcrum-static (grand_seesaw), and lateral /
        ///     tilted contact (church) — the v1 "centroid strictly below" rule wrongly flagged both. A mutually-
        ///     touching pair with no path to a static never grounds, so a real float is still caught.
        /// Convex-only SAT (all pieces + the rectangular floor/cradle statics are convex); no shipped level authors a
        /// concave floor. Sim-only; the production path never runs this.
        /// </summary>
        private void ComputeHonestyMetrics(out int intersectingPairs, out float maxPenetration,
            out int unsupportedPlaced, out WorstOverlapPair worst)
        {
            var epsilon = _tuning.OverlapTolerance;       // the SINGLE shared constant, == the controller gate's
            const float supportProbe = 0.05f;             // a gap this small beneath a piece counts as "resting on"

            intersectingPairs = 0;
            maxPenetration = 0f;
            unsupportedPlaced = 0;
            worst = WorstOverlapPair.None;

            var polys = new List<Vec2[]>();
            var ids = new List<int>();
            var kinds = new List<string>();
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                polys.Add(Geometry.WorldVertices(_model.TypeFor(piece).Shape, piece.Position, piece.Rotation));
                ids.Add(piece.InstanceId);
                kinds.Add(piece.Kind);
            }

            for (var i = 0; i < polys.Count; i++)
            {
                for (var j = i + 1; j < polys.Count; j++)
                {
                    var penetration = Geometry.ConvexPenetration(polys[i], polys[j]);
                    var baseline = _mechanic.OverlapAllowance(ids[i], ids[j]);
                    // Overshoot above what the DESIGN authored for this specific pair of claimed slots.
                    var overshoot = penetration - baseline;
                    if (overshoot > maxPenetration)
                    {
                        maxPenetration = overshoot;
                        // TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.2 — "не гадай, добавь детали худшей пары"):
                        // record the pair driving the worst overshoot so a run flagged on CANON is diagnosable —
                        // raw penetration vs. applied baseline distinguishes physical-settle slop (pen ≈ baseline +
                        // small) from a cross-mapping (baseline for the wrong slot pair), and the two claimed slots
                        // + kinds pin exactly which same-kind instances mis-mapped.
                        worst = new WorstOverlapPair(
                            kinds[i], kinds[j], penetration, baseline,
                            _mechanic.ClaimedSlot(ids[i]), _mechanic.ClaimedSlot(ids[j]));
                    }
                    if (overshoot > epsilon)
                        intersectingPairs++;
                }
            }

            unsupportedPlaced = CountUngrounded(polys, supportProbe);
        }

        /// <summary>Count Placed pieces that are NOT transitively grounded (TASK-042). Seeds groundedness from the
        /// statics, then propagates: any not-yet-grounded piece that, dropped by <paramref name="supportProbe"/>,
        /// touches an already-grounded piece becomes grounded — iterated until the set stops growing. Direction-
        /// agnostic (credits lateral / tilted contact), yet a cluster with no path to a static stays ungrounded.</summary>
        private int CountUngrounded(List<Vec2[]> polys, float supportProbe)
        {
            var statics = _level.StaticGeometry;
            var grounded = new bool[polys.Count];
            var lowered = new Vec2[polys.Count][];
            var remaining = 0;
            for (var i = 0; i < polys.Count; i++)
            {
                lowered[i] = ShiftDown(polys[i], supportProbe);
                if (SupportedByStatic(lowered[i], statics))
                    grounded[i] = true;
                else
                    remaining++;
            }

            var changed = true;
            while (changed && remaining > 0)
            {
                changed = false;
                for (var i = 0; i < polys.Count; i++)
                {
                    if (grounded[i])
                        continue;
                    for (var k = 0; k < polys.Count; k++)
                    {
                        if (k == i || !grounded[k])
                            continue;
                        if (Geometry.ConvexPenetration(lowered[i], polys[k]) > 0f)
                        {
                            grounded[i] = true;
                            remaining--;
                            changed = true;
                            break;
                        }
                    }
                }
            }

            return remaining;
        }

        private static bool SupportedByStatic(Vec2[] lowered, Polygon[] statics)
        {
            if (statics == null)
                return false;
            foreach (var s in statics)
            {
                if (s?.Outline == null || s.Outline.Length < 3)
                    continue;
                if (Geometry.ConvexPenetration(lowered, s.Outline) > 0f)
                    return true;
            }
            return false;
        }

        private static Vec2[] ShiftDown(Vec2[] verts, float d)
        {
            var r = new Vec2[verts.Length];
            for (var i = 0; i < verts.Length; i++)
                r[i] = new Vec2(verts[i].X, verts[i].Y - d);
            return r;
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
