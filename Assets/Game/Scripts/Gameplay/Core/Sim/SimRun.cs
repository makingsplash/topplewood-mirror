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
        // TASK-075: which authored slot pairs are TOUCHING (≥1 circle, |sep|≤ProximityEps in the slot pose), baked
        // beside _slotBaseline by the same classifier. A touching pair is NOT judged against the static baseline
        // (that is ≈0 for circles authored in contact, which physics cannot hold → a false red); its raw resting
        // penetration is emitted for the harness's cross-run reference-rest floor instead. Null until baked.
        private bool[,] _touchingPair;
        // TASK-043: the detector's own tolerances, now sim-local constants (they used to live on ShapeFillTuning and
        // be shared with the controller gate, both removed with the assist layer). Values carried over verbatim:
        // DetectorEpsilon is the interpenetration overshoot above baseline that counts as dishonest; DetectorSupportProbe
        // is the drop distance a Placed piece must touch static/placed support within to count as grounded. TASK-075:
        // DetectorEpsilon is public so the harness applies the SAME intersect threshold to the floored touching pairs.
        public const float DetectorEpsilon = 0.05f;
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

#if GPIE_DEVELOPMENT
        // TASK-070: the SAME per-step pose logger the device build uses, opt-in via the logSteps ctor flag. Reusing
        // the one class (not a sim-local copy) is what GUARANTEES the [STEPLOG] lines are byte-identical to the
        // device side — otherwise there is nothing to diff. It is dev-gated and NEVER attached by the corpus sweep
        // (LevelSimRunner passes logSteps only on its dedicated single-level pass), so the gated report and every
        // poseHash are unchanged. Even if it WERE attached it is read-only (reads the pose mirror, writes only the
        // log), so physics/poseHash cannot move. null when logSteps is false → the controller's StepObserver stays
        // null exactly as in prod/the default sweep.
        private readonly Gameplay.Dev.StepPoseLogger _stepLogger;
#endif

        public SimRun(LevelData level, IShapeFillSimulation simulation, ISimBotPolicy policy, bool logSteps = false)
        {
            _level = level;
            _policy = policy;

            _tuning = new ShapeFillTuning();
            _model = new GameModel();
            _match = new PoseMatchEvaluator();
            _clock = new GameplayClock(_tuning.FixedStep);
            _input = new SimBotInputSource();

            // TASK-085: NullTapTolerance (r=0) keeps the two-tier hit-test containment-only in the sweep — the sim bot
            // taps exact centroids, so tier-1 always hits and the halo tier is inert → poseHash byte-for-byte unchanged.
            _mechanic = new ShapeFillMechanicController(
                _model, simulation, _match, new LevelDataValidator(new TapZoneConfig()), new NullPieceViewFactory(),
                _tuning, new NullTapTolerance());
            _controller = new GameController(
                new FixedLevelSource(level), _model, _mechanic, simulation, _clock, _input);

            _outcomeSub = _model.Outcome.Subscribe(o => _outcome = o);

#if GPIE_DEVELOPMENT
            // Attach BEFORE Start() so the logger's LevelLoaded/LevelResolved subscriptions are live for this run's
            // single load and its terminal verdict (BEGIN is emitted lazily on the first step; END fires on the
            // Won/Lost resolve). Self-attaches as _controller.StepObserver — the same seam the device build uses.
            if (logSteps)
            {
                _stepLogger = new Gameplay.Dev.StepPoseLogger(_controller, _model, _tuning);
                _stepLogger.Start();
            }
#endif

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
                out var worst, out var touchingPens);

            return new SimRunResult(_level.LevelId, _level.Seed, policyId, orderKey, repeatIndex,
                solved, outcome, _model.AttemptsUsed, stepsToResolve, match.TotalError,
                match.SlotsFilled, match.SlotsTotal, lost, SimPoseHash.Of(_model),
                _maxPreGlideAngResidual, _maxPreGlidePosErr, finalPoses, _preGlidePoses,
                intersectingPairs, maxPenetration, unsupportedPlaced, worst, touchingPens);
        }

        /// <summary>
        /// TASK-042 honesty detector — the owner's proof that physics stays honest in EVERY release order — now fully
        /// SIM-LOCAL (TASK-043 decoupled it from the removed controller assist layer, but its checks are unchanged).
        /// Over the FINAL settled (Placed) poses it measures:
        /// (a) INTERPENETRATION — pairs of pieces whose measured penetration exceeds their AUTHORED baseline
        ///     overlap (the sim-local per-pair design overlap baked in <see cref="EnsureSlotBaseline"/>) by more than
        ///     the <c>DetectorEpsilon</c> constant. Measuring the OVERSHOOT above baseline — not raw penetration — is
        ///     what stops authored micro-overlaps (owl's ear in the head, birthday_cake's touching tiers) from being
        ///     false-flagged. <c>maxPenetration</c> is the worst such overshoot (0 ⇒ every contact is within design + slop).
        ///     TASK-075: an authored-TOUCHING pair (circles designed in contact, whose static baseline is ≈0 yet whose
        ///     physical resting-slop is a nonzero honest contact) is NOT judged here — its raw penetration goes to
        ///     <paramref name="touchingPens"/> and the harness measures overshoot above the cross-run reference-rest
        ///     FLOOR instead of above 0. So this run's intersect/overshoot cover only the static-baseline pairs.
        /// (b) FREEZE-IN-AIR — Placed pieces not TRANSITIVELY grounded: a piece is grounded if, dropped by
        ///     <c>SupportProbe</c>, it touches a static OR an already-grounded piece (iterated to a fixed point). This
        ///     credits a load resting on a free beam that rests on a fulcrum-static (grand_seesaw), and lateral /
        ///     tilted contact (church) — the v1 "centroid strictly below" rule wrongly flagged both. A mutually-
        ///     touching pair with no path to a static never grounds, so a real float is still caught. Pieces that have
        ///     fallen OUT of <see cref="_level"/>.Bounds are excluded (TASK-043): they are escapees in free fall (a
        ///     pathological Timeout, e.g. grand_seesaw with the fulcrum never released), not dishonest mid-air freezes.
        /// All pieces + the rectangular floor/cradle statics are convex, so penetration is exact. TASK-075: a Circle
        /// piece is measured as the NATIVE disc the physics simulates (analytic, rotation-invariant) rather than
        /// Shape.Circle's inscribed 24-gon; every other primitive stays a polygon (its collider IS a polygon), so
        /// levels without circles are byte-unchanged. Sim-only; the production path never runs this.
        /// </summary>
        private void ComputeHonestyMetrics(out int intersectingPairs, out float maxPenetration,
            out int unsupportedPlaced, out WorstOverlapPair worst, out List<TouchingPen> touchingPens)
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
            // TASK-075: raw resting penetrations of authored-TOUCHING pairs, handed to the harness for the cross-run
            // reference-rest floor (see field docs). Stays empty on levels with no authored-touching circle pair.
            touchingPens = new List<TouchingPen>();

            // TASK-075: each Placed piece as the convex collider the PHYSICS simulates — a native disc for a Circle
            // piece (centre = body position, angle irrelevant), a world-vertex polygon for every other primitive.
            // Keyed on the SAME predicate as the adapter (UnityShapeFillSimulation:95), so detector and physics agree
            // by construction; HalfDisc/QuarterDisc stay polygons because their collider is a polygon too.
            var shapes = new List<ConvexShape>();
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
                shapes.Add(ConvexShapeOf(_model.TypeFor(piece).Shape, piece.Position, piece.Rotation));
                kinds.Add(piece.Kind);
                slotOf.Add(NearestSameKindSlot(piece.Kind, piece.Position));
                inBounds.Add(_level.Bounds.Contains(piece.Position));
            }

            for (var i = 0; i < shapes.Count; i++)
            {
                for (var j = i + 1; j < shapes.Count; j++)
                {
                    var penetration = Geometry.Penetration(shapes[i], shapes[j]);
                    // TASK-075: an authored-TOUCHING pair (circles designed in contact) is NOT judged here — its
                    // honest baseline is the cross-run reference-rest FLOOR, unknown to a single run. Emit the raw
                    // resting penetration for the harness to floor and fold back; skip the static-baseline verdict so
                    // it neither inflates this run's overshoot nor the intersect count (the harness re-derives both).
                    if (IsTouchingPair(slotOf[i], slotOf[j]))
                    {
                        touchingPens.Add(new TouchingPen(slotOf[i], slotOf[j], kinds[i], kinds[j], penetration));
                        continue;
                    }
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

            unsupportedPlaced = CountUngrounded(shapes, supportProbe, inBounds);
        }

        /// <summary>The convex collider the PHYSICS simulates for a piece/slot at a pose (TASK-075): delegates to the
        /// single shared dispatch <see cref="ConvexShape.Of"/> (native disc for a <c>Circle</c>, world-vertex polygon
        /// otherwise), the SAME one the adapter (UnityShapeFillSimulation:95) and the touching classifier use.</summary>
        private static ConvexShape ConvexShapeOf(Shape shape, Vec2 position, float rotation)
            => ConvexShape.Of(shape, position, rotation);

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
            _touchingPair = new bool[count, count];
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

            // TASK-075: bake with the SAME geometry dispatch as the live pairwise measurement (circle → native disc,
            // else polygon). The symmetry is load-bearing — overshoot = live − baseline, so baking a circle slot as a
            // polygon while measuring it live as a disc would inject a false overshoot on authored-touching circles.
            var hasShape = new bool[count];
            var shapeOf = new ConvexShape[count];
            for (var i = 0; i < count; i++)
                if (shapeByKind.TryGetValue(slots[i].Kind, out var shape))
                {
                    shapeOf[i] = ConvexShapeOf(shape, slots[i].Position, slots[i].Rotation);
                    hasShape[i] = true;
                }

            for (var i = 0; i < count; i++)
            {
                if (!hasShape[i])
                    continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (!hasShape[j])
                        continue;
                    var pen = Geometry.Penetration(shapeOf[i], shapeOf[j]);
                    _slotBaseline[i, j] = pen;
                    _slotBaseline[j, i] = pen;
                    // TASK-075: classify the pair by its signed slot-pose gap. Only TOUCHING (≥1 circle, |sep|≤eps)
                    // is diverted to the reference-rest floor; OVERLAPPING/DISJOINT keep the static baseline above.
                    // Same SlotTouchClassifier the harness prints from, so the gate path and the eyes-on diagnostic
                    // agree by construction.
                    if (SlotTouchClassifier.Classify(shapeOf[i], shapeOf[j], out _) == SlotTouchClass.Touching)
                    {
                        _touchingPair[i, j] = true;
                        _touchingPair[j, i] = true;
                    }
                }
            }
        }

        /// <summary>Whether two nearest-slot indices form an authored-TOUCHING pair (baked in
        /// <see cref="EnsureSlotBaseline"/>) — false for out-of-range/-1 indices.</summary>
        private bool IsTouchingPair(int a, int b)
        {
            if (_touchingPair == null || a < 0 || b < 0 ||
                a >= _touchingPair.GetLength(0) || b >= _touchingPair.GetLength(1))
                return false;
            return _touchingPair[a, b];
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
        private int CountUngrounded(List<ConvexShape> shapes, float supportProbe, List<bool> inBounds)
        {
            var statics = _level.StaticGeometry;
            var grounded = new bool[shapes.Count];
            var lowered = new ConvexShape[shapes.Count];
            var remaining = 0;
            for (var i = 0; i < shapes.Count; i++)
            {
                // TASK-043: a Placed piece that has escaped the world Bounds is in free fall (pathological Timeout,
                // not an air-freeze) — drop it from the grounding graph entirely: neither required to be supported
                // (never added to remaining) nor allowed to serve as support for another piece (never grounded, so
                // never picked as an anchor k below). lowered[i] stays default and is only read for in-Bounds pieces.
                if (!inBounds[i])
                    continue;
                lowered[i] = shapes[i].ShiftedDown(supportProbe);
                if (SupportedByStatic(lowered[i], statics))
                    grounded[i] = true;
                else
                    remaining++;
            }

            var changed = true;
            while (changed && remaining > 0)
            {
                changed = false;
                for (var i = 0; i < shapes.Count; i++)
                {
                    if (grounded[i] || !inBounds[i])
                        continue;
                    for (var k = 0; k < shapes.Count; k++)
                    {
                        if (k == i || !grounded[k])
                            continue;
                        if (Geometry.Penetration(lowered[i], shapes[k]) > 0f)
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

        private static bool SupportedByStatic(ConvexShape lowered, Polygon[] statics)
        {
            if (statics == null)
                return false;
            foreach (var s in statics)
            {
                if (s?.Outline == null || s.Outline.Length < 3)
                    continue;
                // Statics (floor/cradle) are always polygons; a circle piece drops against them via circle-poly.
                if (Geometry.Penetration(lowered, ConvexShape.Polygon(s.Outline)) > 0f)
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
#if GPIE_DEVELOPMENT
            _stepLogger?.Dispose(); // detaches itself from _controller.StepObserver + drops its subscriptions
#endif
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
            string policyId, string orderKey, int repeatIndex, int maxSteps, bool logSteps = false)
        {
            using var run = new SimRun(level, simulation, policy, logSteps);

            var steps = 0;
            for (; steps < maxSteps && !run.Resolved; steps++)
                run.Step();

            var result = run.BuildResult(policyId, orderKey, repeatIndex, steps);
            simulation.Reset();
            return result;
        }
    }
}
