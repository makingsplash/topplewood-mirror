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
        // Passed to the mechanic and used here for FixedStep. TASK-043: it no longer carries the honesty-detector
        // tolerances — the assist layer (and its shared gate constants) were removed, so the detector below owns its
        // own sim-local constants (DetectorEpsilon / DetectorSupportProbe).
        private readonly ShapeFillTuning _tuning;
        private readonly IDisposable _outcomeSub;

        // TASK-043: the honesty detector's authored-baseline overlap map, computed SIM-LOCALLY (the controller no
        // longer owns it after the assist-layer removal). Same bake as the old controller BuildSlotBaseline: pairwise
        // SAT penetration of every two target slots with each Kind's shape at its exact slot pose — the overlap the
        // DESIGN intends (0 for disjoint slots, small positive for authored nestings). Built once, lazily. So the
        // detector still measures OVERSHOOT above the authored baseline, keeping authored micro-overlaps from being
        // false-flagged, but with zero coupling to the controller.
        private float[,] _slotBaseline;
        // TASK-043: the detector's own tolerances, now sim-local constants (they used to live on ShapeFillTuning and
        // be shared with the controller gate, both removed with the assist layer). Values carried over verbatim:
        // DetectorEpsilon is the interpenetration overshoot above baseline that counts as dishonest; DetectorSupportProbe
        // is the drop distance a Placed piece must touch static/placed support within to count as grounded.
        private const float DetectorEpsilon = 0.05f;
        private const float DetectorSupportProbe = 0.1f;
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
        /// One driver iteration. For a rest-gated policy the driver asks it only when the controller reports the
        /// field is ready for the next release group (balance-aware — beam settled AND the coalescence window
        /// closed; assembly ≡ "no Falling piece") — the patient protocol. For a NON-gated policy (TASK-060
        /// <see cref="ScriptedReleasePolicy"/>) the rest-gate is dropped and the policy is asked every Running step;
        /// it times its own releases by fixed-step count, expressing the "fast" game the player can already play.
        /// Either way ALL of a group's taps are pushed in this SAME step so a synchronous pair enters the physics
        /// together (TASK-023 §5.4), and the push still goes through the unchanged input seam — the game's release
        /// rules (<c>OnIntent</c>) are untouched. Then advance exactly one fixed physics step + mechanic tick.
        /// </summary>
        public void Step()
        {
            if (_model.CurrentPhase == GamePhase.Running
                && (!_policy.GatedByRest || _mechanic.ReadyForNextRelease)
                && _policy.TryNextRelease(_model, _tapScratch))
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
                intersectingPairs, maxPenetration, unsupportedPlaced, worst);
        }

        /// <summary>
        /// TASK-042 honesty detector — the owner's proof that physics stays honest in EVERY release order — now fully
        /// SIM-LOCAL (TASK-043 decoupled it from the removed controller assist layer, but its checks are unchanged).
        /// Over the FINAL settled (Placed) poses it measures:
        /// (a) INTERPENETRATION — pairs of pieces whose convex SAT penetration exceeds their AUTHORED baseline
        ///     overlap (the sim-local per-pair design overlap baked in <see cref="EnsureSlotBaseline"/>) by more than
        ///     the <c>DetectorEpsilon</c> constant. Measuring the OVERSHOOT above baseline — not raw penetration — is
        ///     what stops authored micro-overlaps (owl's ear in the head, birthday_cake's touching tiers) from being
        ///     false-flagged. <c>maxPenetration</c> is the worst such overshoot (0 ⇒ every contact is within design + slop).
        /// (b) FREEZE-IN-AIR — Placed pieces not TRANSITIVELY grounded: a piece is grounded if, dropped by
        ///     <c>SupportProbe</c>, it touches a static OR an already-grounded piece (iterated to a fixed point). This
        ///     credits a load resting on a free beam that rests on a fulcrum-static (grand_seesaw), and lateral /
        ///     tilted contact (church) — the v1 "centroid strictly below" rule wrongly flagged both. A mutually-
        ///     touching pair with no path to a static never grounds, so a real float is still caught. Pieces that have
        ///     fallen OUT of <see cref="_level"/>.Bounds are excluded (TASK-043): they are escapees in free fall (a
        ///     pathological Timeout, e.g. grand_seesaw with the fulcrum never released), not dishonest mid-air freezes.
        /// Convex-only SAT (all pieces + the rectangular floor/cradle statics are convex); no shipped level authors a
        /// concave floor. Sim-only; the production path never runs this.
        /// </summary>
        private void ComputeHonestyMetrics(out int intersectingPairs, out float maxPenetration,
            out int unsupportedPlaced, out WorstOverlapPair worst)
        {
            // TASK-043: both tolerances are now sim-local constants (the shared ShapeFillTuning knobs were removed
            // with the assist layer). epsilon = the interpenetration overshoot above the authored baseline that
            // counts as dishonest; supportProbe = the drop gap a Placed piece must touch static/placed support within.
            const float epsilon = DetectorEpsilon;
            const float supportProbe = DetectorSupportProbe;

            EnsureSlotBaseline();

            intersectingPairs = 0;
            maxPenetration = 0f;
            unsupportedPlaced = 0;
            worst = WorstOverlapPair.None;

            var polys = new List<Vec2[]>();
            var kinds = new List<string>();
            // Each Placed piece's nearest same-Kind slot (the sim-local equivalent of the controller's removed
            // ClaimedSlot) — used to look up the authored baseline overlap and to report the worst pair.
            var slotOf = new List<int>();
            // TASK-043 detector-fidelity fix: which Placed pieces are still inside the world Bounds. A piece that has
            // fallen OUT of Bounds (grand_seesaw fulcrum-not-released Timeout: the beam+loads free-fall to y≈-600) is
            // an escapee in free fall, NOT a dishonest mid-air freeze — it must not be counted as unsupported. Uses
            // the SAME _level.Bounds the falling-state gate uses (IShapeFillSimulation.IsWithinBounds), so the two agree.
            var inBounds = new List<bool>();
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                polys.Add(Geometry.WorldVertices(_model.TypeFor(piece).Shape, piece.Position, piece.Rotation));
                kinds.Add(piece.Kind);
                slotOf.Add(NearestSameKindSlot(piece.Kind, piece.Position));
                inBounds.Add(_level.Bounds.Contains(piece.Position));
            }

            for (var i = 0; i < polys.Count; i++)
            {
                for (var j = i + 1; j < polys.Count; j++)
                {
                    var penetration = Geometry.ConvexPenetration(polys[i], polys[j]);
                    var baseline = SlotBaselineBetween(slotOf[i], slotOf[j]);
                    // Overshoot above what the DESIGN authored for this specific pair of nearest slots.
                    var overshoot = penetration - baseline;
                    if (overshoot > maxPenetration)
                    {
                        maxPenetration = overshoot;
                        // TASK-042 gate-v2 diagnostic: record the pair driving the worst overshoot so a run flagged on
                        // CANON is diagnosable — raw penetration vs. applied baseline distinguishes physical rest slop
                        // (pen ≈ baseline + small) from a cross-mapping, and the two slots + kinds pin which same-kind
                        // instances mis-mapped.
                        worst = new WorstOverlapPair(kinds[i], kinds[j], penetration, baseline, slotOf[i], slotOf[j]);
                    }
                    if (overshoot > epsilon)
                        intersectingPairs++;
                }
            }

            unsupportedPlaced = CountUngrounded(polys, supportProbe, inBounds);
        }

        /// <summary>Bake the authored-baseline overlap map once (TASK-043, moved sim-side from the controller): with
        /// each Kind's shape at its exact slot pose, the pairwise SAT penetration of every two slots. This is the
        /// overlap the DESIGN intends (0 for disjoint slots, a small positive for authored nestings), so the detector
        /// measures OVERSHOOT above it and the canonical silhouette is never false-flagged.</summary>
        private void EnsureSlotBaseline()
        {
            if (_slotBaseline != null)
                return;

            var slots = _level.Target?.Slots;
            var count = slots?.Count ?? 0;
            _slotBaseline = new float[count, count];
            if (count == 0)
                return;

            // Kind → shape, from the pieces the level defines (each slot Kind has a matching piece type).
            var shapeByKind = new Dictionary<string, Shape>();
            foreach (var piece in _model.Pieces)
            {
                var type = _model.TypeFor(piece);
                if (type != null && !shapeByKind.ContainsKey(type.Kind))
                    shapeByKind[type.Kind] = type.Shape;
            }

            var worldVerts = new Vec2[count][];
            for (var i = 0; i < count; i++)
                if (shapeByKind.TryGetValue(slots[i].Kind, out var shape))
                    worldVerts[i] = Geometry.WorldVertices(shape, slots[i].Position, slots[i].Rotation);

            for (var i = 0; i < count; i++)
            {
                if (worldVerts[i] == null)
                    continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (worldVerts[j] == null)
                        continue;
                    var pen = Geometry.ConvexPenetration(worldVerts[i], worldVerts[j]);
                    _slotBaseline[i, j] = pen;
                    _slotBaseline[j, i] = pen;
                }
            }
        }

        /// <summary>Authored (design) overlap between two slots — 0 when either index is out of range.</summary>
        private float SlotBaselineBetween(int a, int b)
        {
            if (_slotBaseline == null || a < 0 || b < 0 ||
                a >= _slotBaseline.GetLength(0) || b >= _slotBaseline.GetLength(1))
                return 0f;
            return _slotBaseline[a, b];
        }

        /// <summary>Nearest same-Kind slot to a position (ignores occupancy — the detector only needs a baseline
        /// reference, not a one-slot-one-piece assignment), or -1 when none matches.</summary>
        private int NearestSameKindSlot(string kind, Vec2 pos)
        {
            var slots = _level.Target?.Slots;
            if (slots == null)
                return -1;

            var best = -1;
            var bestErr = float.MaxValue;
            for (var s = 0; s < slots.Count; s++)
            {
                if (slots[s].Kind != kind)
                    continue;
                var err = (slots[s].Position - pos).Length;
                if (err < bestErr)
                {
                    bestErr = err;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Count Placed pieces that are NOT transitively grounded (TASK-042). Seeds groundedness from the
        /// statics, then propagates: any not-yet-grounded piece that, dropped by <paramref name="supportProbe"/>,
        /// touches an already-grounded piece becomes grounded — iterated until the set stops growing. Direction-
        /// agnostic (credits lateral / tilted contact), yet a cluster with no path to a static stays ungrounded.</summary>
        private int CountUngrounded(List<Vec2[]> polys, float supportProbe, List<bool> inBounds)
        {
            var statics = _level.StaticGeometry;
            var grounded = new bool[polys.Count];
            var lowered = new Vec2[polys.Count][];
            var remaining = 0;
            for (var i = 0; i < polys.Count; i++)
            {
                // TASK-043: a Placed piece that has escaped the world Bounds is in free fall (pathological Timeout,
                // not an air-freeze) — drop it from the grounding graph entirely: neither required to be supported
                // (never added to remaining) nor allowed to serve as support for another piece (never grounded, so
                // never picked as an anchor k below). lowered[i] stays null and is only read for in-Bounds pieces.
                if (!inBounds[i])
                    continue;
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
                    if (grounded[i] || !inBounds[i])
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
