using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Structural, deterministic level-schema validator (RFC §7.3). Catches broken schema only — statistical
    /// solvability (pass-rate over seeded strategies) is level-sim's job (GAME_RULES §4), not this.
    /// TASK-007 pivot: target figure + pre-placed pieces + pose tolerance (no coverage/pool checks).
    /// </summary>
    public sealed class LevelDataValidator
    {
        /// <summary>TASK-304: the maximum start-pose overlap between a piece and a static-geometry block still treated
        /// as "resting flush" on the surface (legal). A piece authored to sit EXACTLY on a surface computes a hair of
        /// penetration from vertex quantization (<see cref="Shape"/> quantizes arcs to 1e-4) and the circle-polygon
        /// closest-point, so the threshold must clear that float noise while staying far below any visible embed. 1 mm
        /// is 10× the quantization floor yet ~40× below the shallowest real embed the owner measured on petal_gully
        /// (0.04). Below this ⇒ flush, no error; above ⇒ the piece begins EMBEDDED ⇒ ERROR. Tightening (down) is always
        /// safe; widening it toward a visible overlap is exactly the "loosen the criterion for green" the canon forbids.</summary>
        public const float MinStaticPenetration = 0.001f;

        /// <summary>TASK-325: how short a static-geometry edge may be before it is a DEGENERACY (a duplicated point or a
        /// zero-length segment), not a real edge — 10× the arc-quantization floor (<see cref="Shape"/> quantizes to
        /// 1e-4) so any authored segment always clears it. This is a DEGENERACY guard, NOT the anti-tunnel lower bound
        /// on segment length for a thin surface (that is a balancer number proven by the sim gate, tracked as
        /// tech-debt) — do not conflate the two or widen this toward a gameplay threshold.</summary>
        public const float MinStaticSegment = 1e-4f;

        /// <summary>TASK-318: when the target mosaic is engaged, how far a box/plank dimension may stray from an exact
        /// integer number of grid cells and still be accepted as that many cells. It is a FRACTION OF A CELL (0.02 =
        /// 2% of the pitch). The corpus boxes are exact multiples (mosaic_bloom's 1.38/0.69 = 2.000, 2.07/0.69 = 3.000),
        /// so this only clears authoring/float noise; a piece whose size is a genuinely non-integer number of cells
        /// cannot tile the grid and is rejected (it does not fit). Tightening toward 0 is always safe; widening it
        /// toward 0.5 would begin ALIASING a piece onto the wrong cell count — the "loosen the criterion for green" the
        /// canon forbids.</summary>
        public const float GridMultipleEpsilon = 0.02f;

        /// <summary>TASK-318: how far a target slot's rotation may stray from a multiple of 90° and still be treated as
        /// axis-aligned (radians). A grid mosaic tiles only at right angles; the corpus slots are authored at exactly
        /// 0/±π-2 so this only absorbs the float noise of an authored quarter-turn. A slot rotated off the right-angle
        /// lattice cannot land on the cell grid, so it is an authoring error, not something to widen this past a hair.</summary>
        public const float GridRotationEpsilon = 0.01f;

        /// <summary>TASK-318: how far a mosaic cell may sit from its nearest grid node (as a FRACTION OF A CELL) before
        /// it is reported off-grid. The grid ORIGIN is inferred from the data (median cell phase, robust to a stray
        /// violator), not pinned to world-zero, so this measures real misalignment, not a chosen offset. 0.25 = a
        /// quarter cell: it absorbs the author's layout drift (mosaic_bloom lays slots on a 0.70 step while the piece
        /// cell is 0.69 → ≤0.08 cell of drift across the figure) while staying well below 0.5, where a cell would alias
        /// onto its NEIGHBOUR and a real off-grid piece would read as on-grid. Widening toward 0.5 is exactly the
        /// forbidden "loosen the criterion"; tightening is safe.</summary>
        public const float GridSnapEpsilon = 0.25f;

        /// <summary>TASK-318: cap on how many offending cells of each kind (overlaps / holes) a single load error
        /// enumerates — enough to draw the author the map (the owner's improvised dump listed ~12) without an unbounded
        /// message. The COUNT is always reported; only the per-cell enumeration is capped.</summary>
        private const int MaxReportedMosaicCells = 12;

        /// <summary>TASK-340: how far a box/plank's width may stray from its height (RELATIVE, fraction of the larger
        /// side) and still be judged a true SQUARE — a square has 4-fold rotational symmetry, a rectangle only 2-fold,
        /// and that distinction is what makes <see cref="PieceType.SymmetryOrder"/> 4 sound or a silent judging lie. It
        /// is DELIBERATELY tight (1e-3): authored true squares are exact (0.6×0.6, 2.72×2.72) so any hair of ε passes
        /// them, while TASK-339's 2.70×2.75 (1.8% off) MUST fail — that mismatch was the bug. Do NOT reuse the 2%
        /// <see cref="GridMultipleEpsilon"/> here: it would wave the exact defect through. Tightening toward 0 is always
        /// safe; widening it toward accepting a visibly non-square box is the "loosen the criterion for green" the canon
        /// forbids.</summary>
        public const float SquareTolerance = 1e-3f;

        /// <summary>TASK-340 phase gate, now in PHASE 2. <c>true</c> ⇒ a symmetryOrder/shape mismatch loads as an ERROR
        /// and BLOCKS the level (phase 1 was a non-blocking WARNING while the corpus still carried ~24 non-square boxes
        /// authored order 4). Flipped to <c>true</c> by TASK-342 after the corpus-cleanup task (TASK-345) re-authored
        /// every violator 4→2 and the full sweep landed 0 symmetry warnings (27/27 green). Flipping was a one-line change
        /// tracked by the same unit test (it asserts routing against THIS flag). Widening the RULE (accepting a
        /// non-dividing order) is never correct; this flag only moves an already-detected mismatch between WARNING and
        /// ERROR. Mirrors the canon-blessed TASK-085 tap-spacing rollout
        /// (<see cref="TapZoneConfig.EnforceMinDistanceAsError"/>), which phased in for the same reason.</summary>
        public const bool SymmetryOrderMismatchIsError = true;

        /// <summary>TASK-628: how far a PREPLACED piece may start above its support and still count as "resting on it".
        /// It is the physics' own contact tolerance — <c>Physics2D.defaultContactOffset</c> = 0.01 in
        /// ProjectSettings/Physics2DSettings.asset: bodies closer than that are already in contact for the solver, so a
        /// smaller gap never reads as a fall. Above it the piece visibly drops at t=0 (potted_tree had 0.05 and 0.10).
        /// Tightening is safe; widening it to hide a visible drop is the "loosen the criterion for green" the canon
        /// forbids.</summary>
        public const float PreplacedRestTolerance = 0.01f;

        /// <summary>TASK-628: march step of the straight-down drop probe in <see cref="PreplacedDrop"/>. Far below the
        /// thinnest piece (≥0.1) so no support is stepped over, then refined by bisection to a hair.</summary>
        private const float DropProbeStep = 0.01f;

        private readonly TapZoneConfig _tapZone;

        /// <summary>TASK-085: takes the tap-zone config so the tap-spacing check can predict the per-level zone. The
        /// null-coalesce keeps a bare <c>new LevelDataValidator()</c> (older tests/tools) working with defaults.</summary>
        public LevelDataValidator(TapZoneConfig tapZone = null)
        {
            _tapZone = tapZone ?? new TapZoneConfig();
        }

        public ValidationResult Validate(LevelData level)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (level == null)
            {
                errors.Add("LevelData is null.");
                return new ValidationResult(errors, warnings);
            }

            // 1. Target figure: outline ≥3 verts, non-empty slots.
            ValidateTarget(level, errors);

            // 2. Pieces: non-empty, each shape ≥3 verts, non-negative material, start pose in Bounds.
            ValidatePieces(level, errors);

            // 3. Kind coverage: for every slot Kind there must be at least as many pieces of that Kind.
            ValidateKindCoverage(level, errors);

            // 4. Tolerance: PosTol > 0, AngTol ≥ 0, RigidityTol ≥ 0 (0 = constraint off, TASK-043).
            if (level.MatchTolerance.PosTol <= 0f)
                errors.Add($"MatchTolerance.PosTol must be > 0; was {level.MatchTolerance.PosTol}.");
            if (level.MatchTolerance.AngTol < 0f)
                errors.Add($"MatchTolerance.AngTol must be ≥ 0; was {level.MatchTolerance.AngTol}.");
            if (level.MatchTolerance.RigidityTol < 0f)
                errors.Add($"MatchTolerance.RigidityTol must be ≥ 0; was {level.MatchTolerance.RigidityTol}.");

            // 5. Bounds / gravity.
            if (level.Bounds.IsDegenerate)
                errors.Add("Bounds are degenerate.");
            if (level.Gravity.SqrLength <= 0f)
                errors.Add("Gravity must be non-zero.");

            // 6. Balance / Catapult mode: structural shape of a pivot-beam level (winnability is level-sim's job).
            // A catapult (TASK-025) has the same structural invariant as a seesaw — exactly one pivot arm judged
            // from one in-Bounds slot — so it reuses ValidateBalance.
            // A pure-physics level (TASK-031d, freezeLoadsAtRest) may have no marked pivot at all — its lever is an
            // unmarked preplaced piece judged from its physical rest pose, not a snapped pivot beam — so relax the
            // "at least one pivot" requirement for it while keeping the strict check for the 5 catapult + 3 seesaw.
            if (level.PhysicsMode is PhysicsMode.Balance or PhysicsMode.Catapult)
                ValidateBalance(level, errors, requirePivot: !level.FreezeLoadsAtRest);

            // 7. Tap spacing (TASK-085): two tapable (non-preplaced) pieces whose start poses sit closer than the
            //    minimum tap zone are hard to hit apart. Phase 1 = non-blocking WARNING (does not affect Ok).
            ValidateTapSpacing(level, errors, warnings);

            // 8. Air-flow zones (TASK-097): structural well-formedness + the hover-guard that forbids a metastable
            //    up-flow (verdict is judged on global rest — a piece held aloft never rests → Timeout, not a verdict).
            ValidateAirZones(level, errors, warnings);

            // 9. Certified winning order (TASK-237): OPTIONAL. Null → OK (no hint). If present, every index must be
            //    in range, reference a NON-preplaced piece, and be free of duplicates — a bad order is authoring-brak
            //    (the hint would point at a preplaced/absent piece), exactly like an unknown physicsMode.
            ValidateWinningOrder(level, errors);

            // 10. Static geometry well-formedness (TASK-325 / TASK-307): every StaticGeometry block must be a simple
            //     contour before its collider is built — enough vertices, no degenerate edges, no self-intersection,
            //     and (the TASK-307 gate) no CONCAVE filled solid (which Box2D would split coordinate-dependently).
            ValidateStaticGeometry(level, errors);

            // 11. Start-inside-static (TASK-304): a piece must not BEGIN embedded in static geometry. It is invisible
            //     to every check above and to the sim gate (an Idle piece is not in physics, so penetration is only
            //     ever measured on RELEASED bodies) — the exact "green gate, visually broken" the owner hit on
            //     petal_gully (a disk half-sunk into the ramp). ERROR with the piece name and the depth as a number.
            ValidateStaticStartOverlap(level, errors);

            // 12. Target mosaic (TASK-318): when the target is a grid-cell mosaic — a polyomino piece declares the cell
            //     pitch and EVERY slot is a grid-family piece (polyomino/box/plank) — the union of the pieces IN THEIR
            //     TARGET POSES must be an honest tiling: no overlaps, no interior holes, cells summing to distinct. A
            //     physically un-buildable target (mosaic_bloom stacked pieces 4-deep with holes) passes every other
            //     gate AND the sim (which measures only START poses, never target.slots) — the owner caught it by eye,
            //     last in the chain, five runs running. ERROR with the offending cells, exactly like a duplicate levelId.
            ValidateTargetMosaic(level, errors);

            // 13. symmetryOrder ↔ shape (TASK-340): a piece's symmetryOrder claims the judge may fold its pose angle by
            //     2π/order (PoseMatchEvaluator.SymmetryReduce); that is sound only if the shape actually HAS that
            //     rotational symmetry. An over-claim (a non-square box authored order 4) makes the judge silently accept
            //     a 90°-rotated (physically transposed) landing as a match — "тихая порча судейства", invisible to every
            //     check above and to the sim (which never inspects symmetryOrder). Phase 1 = non-blocking WARNING; the
            //     sim sweep surfaces it under [LEVEL-WARN] so it is no longer silent (LevelSimRunner.AuditTapSpacing).
            ValidateSymmetryOrder(level, errors, warnings);

            // 14. Slot reachability (TASK-332): a target slot whose CENTRE is buried inside static geometry can never be
            //     assembled — the piece would be ≥half-submerged in the floor/static. fan_quilt and chute_cross passed
            //     the schema and coverage checks at 0% winnable because the lower half of the target sat inside the
            //     floor; every check above AND the sim (which measures only START poses, never target.slots) were blind
            //     to it. Keyed on the slot CENTRE, NOT collider penetration: a slot legitimately overlaps a decorative
            //     static by a hair (locomotive/petal_gully ship winnable), which a penetration threshold cannot tell
            //     from a deep cut-through. ERROR named with the slot and its centre.
            ValidateSlotReachability(level, errors);

            // 15. Co-release groups (TASK-374): OPTIONAL. Null → OK (no groups). If present, every index must be in
            //     range, reference a NON-preplaced (tapable) piece, be unique across ALL groups, and each group must
            //     hold ≥2 members — a group of one is just an ordinary immediate-release piece and declaring it is an
            //     authoring mistake (a typo that would make the piece unreleasable). Same authoring-brak class as an
            //     out-of-range WinningOrder index.
            ValidateCoReleaseGroups(level, errors, warnings);

            // 16. Preplaced rest (TASK-628): a preplaced piece is level furniture the player sees from t=0 — authored
            //     a hair ABOVE its support, it visibly drops at level start (potted_tree: stick 0.10 over the block,
            //     crown 0.05 over the stick). Invisible to #11 (no overlap) and to the sim (it just settles). WARNING
            //     with the gap as a number and the support named; surfaced by the sweep under [LEVEL-WARN].
            ValidatePreplacedRest(level, warnings);

            return new ValidationResult(errors, warnings);
        }

        /// <summary>
        /// TASK-085 (owner directive 2026-08-04): tapable pieces must not start closer than the minimum tap zone, or
        /// their touch targets merge and small pieces become unhittable. The zone is a SCREEN size (dp) turned into
        /// world units via the level's PREDICTED orthoSize (<see cref="CameraFramingMath"/> replayed on the reference
        /// device) — the same framing the live camera applies — so the check tracks what the player actually sees. The
        /// guard is on START poses of non-<c>Preplaced</c> pieces (the only tapable ones; preplaced pieces are Dynamic
        /// furniture, never tapped).
        ///
        /// TASK-087, metric v2 (v1 centre-to-centre was wrong both ways: it missed card_house, whose long planks have
        /// far centres but ends ≈3dp apart, and false-flagged parallel vertical planks). The idle polygon a finger hits
        /// (<c>PickIdlePieceAt</c> on the start pose) is compared EDGE-TO-EDGE. Rule 1, ANY two tapable pieces:
        /// <see cref="Geometry.PolygonGap"/> &lt; minTapWorld ⇒ violation — the resolver inflates each piece by a halo of
        /// ½·MinTapDp, so the two tap targets overlap exactly below a gap of MinTapDp. Rule 2, members of ONE co-release
        /// group only: centre distance &lt; <see cref="TapZoneConfig.MinCoReleaseSpreadDp"/> ⇒ violation (two fingers at
        /// once must fit and stay two touches). Both rules apply to a pair while
        /// <see cref="TapZoneConfig.JudgeCoReleasePairs"/> is on (default).
        ///
        /// TASK-181: this is the STRUCTURAL, side-effect-free heart of the check — it returns the audit as data (see
        /// <see cref="TapSpacingAudit"/>) so the sim sweep can serialise it into its report, not only log it. It is
        /// PHASE-INDEPENDENT: it just enumerates the too-close pairs; whether they load as WARNINGS or ERRORS is the
        /// caller's decision, driven by <see cref="TapZoneConfig.EnforceMinDistanceAsError"/> in
        /// <see cref="ValidateTapSpacing"/>. An early exit (&lt;2 tapable pieces, or content that will not frame)
        /// returns an empty audit with MinTapWorld=0.
        /// </summary>
        public TapSpacingAudit AuditTapSpacing(LevelData level)
        {
            if (level?.Pieces == null || level.Pieces.Count < 2)
                return new TapSpacingAudit(level?.LevelId, 0f, 0f, 0f, null);
            // TASK-539: the same frame the live camera applies, hint-button corner reserve included (safe-area zero —
            // the reference device has no inset to predict). TASK-563: and the top-corner buttons' reserve.
            if (!CameraFramingMath.TryFrame(level, _tapZone.ReferenceAspect, _tapZone.RefMargin,
                    _tapZone.RefPreviewBand, _tapZone.RefMinOrthoSize, HudCornerZone.HintButton, 0f, 0f,
                    HudCornerZone.TopButtons, default, out var orthoSize, out _))
                return new TapSpacingAudit(level.LevelId, 0f, 0f, 0f, null);
            var minWorld = _tapZone.MinTapDp * _tapZone.RefKDevice * orthoSize; // tap-zone DIAMETER in world units
            var spreadWorld = _tapZone.MinCoReleaseSpreadDp * _tapZone.RefKDevice * orthoSize; // two-finger spread

            // The polygon a finger hits on the start pose (PickIdlePieceAt), once per piece. Null = not tapable
            // (preplaced furniture) or shapeless (already an error from ValidatePieces) — skipped, never an NRE.
            var count = level.Pieces.Count;
            var polys = new Vec2[count][];
            for (var i = 0; i < count; i++)
            {
                var p = level.Pieces[i];
                if (!p.Preplaced && p.Type?.Shape != null)
                    polys[i] = Geometry.WorldVertices(p.Type.Shape, p.StartPosition, p.StartRotation);
            }
            var groupOf = CoReleaseGroupIndex(level, count);

            var violations = new List<TapSpacingViolation>();
            for (var i = 0; i < count; i++)
            {
                if (polys[i] == null)
                    continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (polys[j] == null)
                        continue;
                    var sameGroup = groupOf[i] >= 0 && groupOf[i] == groupOf[j];
                    if (sameGroup && !_tapZone.JudgeCoReleasePairs)
                        continue;

                    var gap = Geometry.PolygonGap(polys[i], polys[j]);
                    var centre = (level.Pieces[i].StartPosition - level.Pieces[j].StartPosition).Length;
                    var kindI = level.Pieces[i].Type.Kind;
                    var kindJ = level.Pieces[j].Type.Kind;
                    if (gap < minWorld)
                        violations.Add(new TapSpacingViolation(
                            i, kindI, j, kindJ, TapSpacingRule.Gap, gap, centre, sameGroup));
                    if (sameGroup && centre < spreadWorld)
                        violations.Add(new TapSpacingViolation(
                            i, kindI, j, kindJ, TapSpacingRule.CoReleaseSpread, gap, centre, true));
                }
            }
            return new TapSpacingAudit(level.LevelId, minWorld, orthoSize, spreadWorld, violations);
        }

        /// <summary>TASK-087: placement index → co-release group id (−1 = in no group). Null-safe; out-of-range indices
        /// are ignored here (<see cref="ValidateCoReleaseGroups"/> reports them as errors).</summary>
        private static int[] CoReleaseGroupIndex(LevelData level, int pieceCount)
        {
            var groupOf = new int[pieceCount];
            for (var i = 0; i < pieceCount; i++)
                groupOf[i] = -1;
            var groups = level.CoReleaseGroups;
            if (groups == null)
                return groupOf;
            for (var g = 0; g < groups.Count; g++)
            {
                if (groups[g] == null)
                    continue;
                foreach (var index in groups[g])
                    if (index >= 0 && index < pieceCount)
                        groupOf[index] = g;
            }
            return groupOf;
        }

        /// <summary>
        /// Phase-1/2 policy wrapper around <see cref="AuditTapSpacing"/>: formats each structural violation into the
        /// author-facing message and routes it to <paramref name="errors"/> or <paramref name="warnings"/> per
        /// <see cref="TapZoneConfig.EnforceMinDistanceAsError"/> (false = non-blocking WARNING, this task's phase 1;
        /// true = hard ERROR, phase 2 after the corpus is cleaned). TASK-087: the text changed with metric v2 (one
        /// message per rule). The "Tap spacing:" PREFIX is kept verbatim — the sim sweep keys on it to count tap
        /// violations (<c>LevelSimRunner.AuditTapSpacing</c>).
        /// </summary>
        private void ValidateTapSpacing(LevelData level, List<string> errors, List<string> warnings)
        {
            var audit = AuditTapSpacing(level);
            foreach (var v in audit.Violations)
            {
                var msg = v.Rule == TapSpacingRule.CoReleaseSpread
                    ? $"Tap spacing: co-release pair Pieces[{v.IndexA}] '{v.KindA}' and Pieces[{v.IndexB}] " +
                      $"'{v.KindB}' centres {v.CentreDistance:F3} apart — closer than the " +
                      $"{audit.CoReleaseSpreadWorld:F3} world two-finger spread ({_tapZone.MinCoReleaseSpreadDp}dp @ " +
                      $"orthoSize {audit.OrthoSize:F2}); two simultaneous fingers will not fit / may merge into one touch."
                    : $"Tap spacing: Pieces[{v.IndexA}] '{v.KindA}' and Pieces[{v.IndexB}] " +
                      $"'{v.KindB}'{(v.SameGroup ? " (co-release pair)" : "")} start {v.Gap:F3} apart edge-to-edge " +
                      $"(centres {v.CentreDistance:F3}) — closer than the {audit.MinTapWorld:F3} world tap zone " +
                      $"({_tapZone.MinTapDp}dp @ orthoSize {audit.OrthoSize:F2}); their tap targets overlap.";
                if (_tapZone.EnforceMinDistanceAsError)
                    errors.Add(msg);
                else
                    warnings.Add(msg);
            }
        }

        /// <summary>
        /// TASK-340: every piece's authored <see cref="PieceType.SymmetryOrder"/> must DIVIDE the shape's true
        /// rotational-symmetry order. The judge (<see cref="PoseMatchEvaluator"/>'s <c>SymmetryReduce</c>) folds the pose
        /// angle by <c>2π/order</c>; that fold is a real symmetry ONLY when rotating the piece by <c>2π/order</c> maps
        /// its silhouette onto itself, i.e. when <c>2π/order</c> is a whole multiple of <c>2π/trueMax</c>, i.e. when
        /// <c>order</c> divides <c>trueMax</c>. Divisibility, NOT <c>≤</c>: a proper divisor (order 1 on a square) is
        /// merely a stricter judge — it can only reject, never falsely accept — so it is legal and must not be flagged;
        /// an over-claim or a non-dividing order (3 on a square, 4 on a rectangle) is the defect and is reported with a
        /// NAMED cause. Routes to <paramref name="warnings"/> in phase 1, <paramref name="errors"/> in phase 2, per
        /// <see cref="SymmetryOrderMismatchIsError"/> (mirrors <see cref="ValidateTapSpacing"/>). Circle is EXEMPT — a
        /// round piece has no meaningful angle, so the corpus authors a high order deliberately to make the judge
        /// rotation-agnostic; requiring <c>order | segments</c> would add a fragile constraint with zero gameplay value.
        /// A null type/shape is already reported by <see cref="ValidatePieces"/>, so it is skipped here (no NRE).
        /// </summary>
        private static void ValidateSymmetryOrder(LevelData level, List<string> errors, List<string> warnings)
        {
            if (level.Pieces == null)
                return;

            // The phase flag is a compile-time const; copy it to a local so neither the ERROR nor the WARNING branch
            // below reads as unreachable dead code for whichever value the const holds. Now true (phase 2, TASK-342).
            var mismatchIsError = SymmetryOrderMismatchIsError;

            for (var i = 0; i < level.Pieces.Count; i++)
            {
                var type = level.Pieces[i].Type;
                if (type?.Shape == null)
                    continue; // already reported by ValidatePieces — do not NRE here
                var shape = type.Shape;

                if (shape.Primitive == ShapePrimitive.Circle)
                    continue; // exempt: a round piece has no meaningful angle (see summary)

                var order = type.SymmetryOrder; // clamped ≥1 in the PieceType ctor, so this is never 0
                var trueMax = TrueMaxSymmetryOrder(shape);
                if (trueMax % order == 0)
                    continue; // order divides trueMax ⇒ the fold is a real symmetry ⇒ legal

                var msg =
                    $"Pieces[{i}] '{type.Kind}' ({DescribeShape(shape)}) authors symmetryOrder {order}, but that shape " +
                    $"has {trueMax}-fold rotational symmetry — the legal orders are {DivisorsText(trueMax)} " +
                    "(symmetryOrder must DIVIDE the shape's true symmetry). PoseMatchEvaluator folds the pose angle by " +
                    $"2π/{order}, so order {order} silently accepts a rotated, physically-different landing as a match.";
                if (mismatchIsError)
                    errors.Add(msg);
                else
                    warnings.Add(msg);
            }
        }

        /// <summary>TASK-340: the true rotational-symmetry order of a shape's silhouette, keyed on its primitive (the
        /// source of truth is each <see cref="Shape"/> factory's docstring — transcribed here, not re-derived by a
        /// geometric self-map test, because several primitives are symmetric about their CENTROID, not their transform
        /// origin, e.g. a parallelogram; an origin-based vertex test would falsely reject its documented order 2). A
        /// box/plank is 4-fold iff it is a true square, else 2-fold (a rectangle always self-coincides at 180°). Circle
        /// is handled by the caller (exempt) and never reaches this. Every primitive not listed has only the trivial
        /// symmetry (order 1): triangle, half-disc, trapezoid, right-triangle, quarter-disc, gable, and the L/J/T
        /// tetrominoes and T/N/V/P/L pentominoes.</summary>
        private static int TrueMaxSymmetryOrder(Shape shape)
        {
            switch (shape.Primitive)
            {
                case ShapePrimitive.Box:
                case ShapePrimitive.Plank:
                    return IsSquareBox(shape) ? 4 : 2;
                case ShapePrimitive.Parallelogram:
                case ShapePrimitive.STetromino:
                case ShapePrimitive.ZTetromino:
                case ShapePrimitive.ZPentomino:
                    return 2;
                case ShapePrimitive.PlusPentomino:
                    return 4;
                case ShapePrimitive.Freeform:
                    // TASK-360: a freeform has no catalogue entry — judge its symmetry GEOMETRICALLY from the contour.
                    return FreeformTrueSymmetry(shape);
                default:
                    return 1;
            }
        }

        // Every k in 12..2, descending: odd orders (a 5-point star, a pentagon, a 7-petal flower) are real silhouettes,
        // and a list without them forces such a shape to order 1 — the judge then fails a 72°-turned star that looks
        // identical (TASK-474, bead_necklace v2).
        private static readonly int[] FreeformSymmetryCandidates = { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        /// <summary>TASK-360: the true rotational-symmetry order of a FREEFORM silhouette, judged GEOMETRICALLY (it has
        /// no catalogue row): the LARGEST k ∈ 12..2 for which rotating the contour by 2π/k about its AREA
        /// centroid carries the vertex set onto itself (within ε), else 1. About the centroid, NOT the transform origin,
        /// so a shape symmetric about its centre is not falsely rejected — the same parallelogram trap the switch
        /// docstring warns of. Order 1 (the chiral default of a figurative detail) always passes; a genuine 2-/4-fold
        /// silhouette is admitted so an author may legally set that order (and the divisibility rule of TASK-340 stays
        /// in force — a claimed order must divide this true max).</summary>
        private static int FreeformTrueSymmetry(Shape shape)
        {
            var verts = shape.Vertices;
            if (verts == null || verts.Length < 3)
                return 1;
            var centroid = PolygonCentroid(verts);
            var ext = 0f;
            foreach (var v in verts)
                ext = System.MathF.Max(ext, (v - centroid).Length);
            var eps = System.MathF.Max(ext, 1e-4f) * 1e-3f;
            foreach (var k in FreeformSymmetryCandidates)
                if (RotationMapsOntoSelf(verts, centroid, k, eps))
                    return k;
            return 1;
        }

        /// <summary>Does rotating every vertex of <paramref name="verts"/> by 2π/<paramref name="k"/> about
        /// <paramref name="centroid"/> land it on some original vertex (within <paramref name="eps"/>)? Rotation is a
        /// bijection of the plane and both sets have the same cardinality, so an onto match is a genuine self-map. O(n²),
        /// n a handful.</summary>
        private static bool RotationMapsOntoSelf(Vec2[] verts, Vec2 centroid, int k, float eps)
        {
            var angle = 2f * System.MathF.PI / k;
            foreach (var v in verts)
            {
                var r = (v - centroid).Rotate(angle) + centroid;
                var matched = false;
                foreach (var w in verts)
                    if ((w - r).Length <= eps)
                    {
                        matched = true;
                        break;
                    }
                if (!matched)
                    return false;
            }
            return true;
        }

        /// <summary>Area (shoelace) centroid of a simple polygon — the fixed point of any rotational symmetry it has
        /// (unlike the vertex mean, which drifts with vertex density). Falls back to the vertex mean if the polygon is
        /// degenerate (near-zero area).</summary>
        private static Vec2 PolygonCentroid(Vec2[] v)
        {
            float twiceArea = 0f, cx = 0f, cy = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                var cross = v[j].X * v[i].Y - v[i].X * v[j].Y;
                twiceArea += cross;
                cx += (v[j].X + v[i].X) * cross;
                cy += (v[j].Y + v[i].Y) * cross;
            }
            if (System.MathF.Abs(twiceArea) < 1e-9f)
            {
                var mean = Vec2.Zero;
                foreach (var p in v)
                    mean += p;
                return mean * (1f / v.Length);
            }
            var inv = 1f / (3f * twiceArea);
            return new Vec2(cx * inv, cy * inv);
        }

        /// <summary>TASK-340: is a box/plank a true square? Its collider bbox equals the authored width×height exactly
        /// (axis-aligned, centred on origin — <see cref="Shape.Box"/>/<see cref="Shape.Plank"/>), so the square test is
        /// on the bbox sides within <see cref="SquareTolerance"/> (relative). Reads <see cref="Shape.Vertices"/>, not an
        /// authored width/height (the domain <see cref="Shape"/> does not retain them).</summary>
        private static bool IsSquareBox(Shape shape)
        {
            BoxBounds(shape, out var w, out var h);
            return System.MathF.Abs(w - h) <= SquareTolerance * System.MathF.Max(w, h);
        }

        /// <summary>Axis-aligned bounding-box extent of a shape's vertices. For a box/plank this equals the authored
        /// width×height; used for the square test and the author-facing message dims.</summary>
        private static void BoxBounds(Shape shape, out float width, out float height)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var verts = shape.Vertices;
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            width = maxX - minX;
            height = maxY - minY;
        }

        /// <summary>TASK-340: a short shape description for the mismatch message — the primitive name, plus the
        /// bbox dims for a box/plank (the only primitive whose square-ness is the whole point of the rule).</summary>
        private static string DescribeShape(Shape shape)
        {
            var name = shape.Primitive.ToString().ToLowerInvariant();
            if (shape.Primitive != ShapePrimitive.Box && shape.Primitive != ShapePrimitive.Plank)
                return name;
            BoxBounds(shape, out var w, out var h);
            return $"{name} {w:F3}×{h:F3}";
        }

        /// <summary>TASK-340: the divisors of <paramref name="trueMax"/> as an author-facing list ("1", "1 or 2",
        /// "1, 2 or 4") — the set of legal symmetry orders for a shape with that true symmetry.</summary>
        private static string DivisorsText(int trueMax)
        {
            var divs = new List<int>();
            for (var d = 1; d <= trueMax; d++)
                if (trueMax % d == 0)
                    divs.Add(d);
            if (divs.Count == 1)
                return divs[0].ToString();
            return string.Join(", ", divs.GetRange(0, divs.Count - 1)) + " or " + divs[divs.Count - 1];
        }

        /// <summary>
        /// Balance-mode structural checks (TASK-021, free-beam TASK-023): the level must have at least one pivot
        /// (beam) piece, and every pivot Kind must map to exactly ONE in-Bounds slot — the beam is judged against a
        /// single rest/judge slot, so two slots of a pivot Kind (or none) is a broken authoring, not a solvable
        /// level. Winnability under the live free beam is proven by level-sim, not here.
        /// </summary>
        private static void ValidateBalance(LevelData level, List<string> errors, bool requirePivot)
        {
            if (level.Pieces == null)
                return;

            var pivotKinds = new HashSet<string>();
            foreach (var placement in level.Pieces)
                if (placement.Type != null && placement.Type.Pivot)
                    pivotKinds.Add(placement.Type.Kind);

            if (pivotKinds.Count == 0)
            {
                // Pure-physics (freezeLoadsAtRest) tolerates no marked pivot: the lever is an unmarked preplaced
                // piece judged from its rest pose. Otherwise a balance/catapult level with no hinge beam is broken.
                if (requirePivot)
                    errors.Add("PhysicsMode.Balance requires at least one piece with pivot=true (the hinge beam).");
                return;
            }

            var slots = level.Target?.Slots;
            foreach (var kind in pivotKinds)
            {
                var count = 0;
                var anchor = default(Vec2);
                if (slots != null)
                    foreach (var slot in slots)
                        if (slot.Kind == kind)
                        {
                            count++;
                            anchor = slot.Position;
                        }

                if (count == 0)
                    errors.Add($"Pivot kind '{kind}' has no target slot to judge its rest pose.");
                else if (count > 1)
                    errors.Add($"Pivot kind '{kind}' has {count} slots; a beam needs exactly one judge slot.");
                else if (!level.Bounds.Contains(anchor))
                    errors.Add($"Pivot kind '{kind}' beam judge slot {anchor} is outside Bounds.");
            }
        }

        /// <summary>
        /// TASK-097 air-flow zone validation. Null ⇒ no flow ⇒ skip (back-compat: every pre-TASK-097 level). Each
        /// zone must be a non-degenerate region with a non-zero direction and positive acceleration. The load-bearing
        /// rule is the HOVER-GUARD: an up-flow whose vertical acceleration component reaches |gravity| lifts a piece
        /// into free-space equilibrium (thrust ≥ weight) so it never comes to rest — the verdict is judged on GLOBAL
        /// rest, so such a level hangs into Timeout instead of resolving (GAME_RULES §4 / ADR-0001 metastability).
        /// This is mass-independent because the acceleration is applied mass-independently (RFC §2.2), so it can be
        /// proven statically here, before any sim run. |gravity| is read from the level (robust to any gravity, not a
        /// hardcoded 9.81); Direction is a unit vector (normalised at DTO→domain), so Direction.Y·Acceleration is the
        /// up-component of the applied acceleration. Pure-horizontal flow (Direction.Y == 0) always passes this rule.
        /// The authoring band [2,4] (balancer) is guidance in level-authoring §L(air), NOT a hard cap — a level's
        /// acceleration is ultimately proven by the sim gate, not clamped here (owner decision, stage-3 gate).
        /// TASK-107: the hover-guard is no longer absolute — a zone with <see cref="AirZone.BypassHoverGuard"/>=true
        /// opts out of THIS one check (owner decision 2026-08-05) so a real up-launcher may lift a piece. The opt-out
        /// does NOT relax the rule or shift the threshold; it moves the burden of proving the level still reaches rest
        /// from this static guard to the sim gate (§L(air) Rule 4). A bypassed up-flow logs a WARNING (not an ERROR) so
        /// the shifted burden is visible at load. Every OTHER structural check below applies regardless of the flag.
        /// </summary>
        private static void ValidateAirZones(LevelData level, List<string> errors, List<string> warnings)
        {
            if (level.AirZones == null)
                return;

            var gravityMag = level.Gravity.Length;
            for (var i = 0; i < level.AirZones.Length; i++)
            {
                var zone = level.AirZones[i];

                if (zone.Region.IsDegenerate)
                    errors.Add($"AirZones[{i}] region is degenerate (zero width or height) — invisible/zero force.");
                if (zone.Direction.SqrLength <= 0f)
                    errors.Add($"AirZones[{i}] direction is zero — a flow zone needs a direction.");
                if (zone.Acceleration <= 0f)
                    errors.Add($"AirZones[{i}] acceleration must be > 0; was {zone.Acceleration}.");

                // Hover-guard: up-component of the applied acceleration must stay below gravity, or the piece hovers —
                // UNLESS the zone opts out (TASK-107). The opt-out does NOT relax the rule; it moves the burden of
                // proving the level still reaches rest from this static check to the sim gate (§L(air) Rule 4).
                var upThrustExceedsGravity = zone.Direction.Y * zone.Acceleration >= gravityMag;
                if (upThrustExceedsGravity && !zone.BypassHoverGuard)
                    errors.Add(
                        $"AirZones[{i}] up-flow is too strong: Direction.Y·Acceleration = " +
                        $"{zone.Direction.Y * zone.Acceleration:F3} ≥ |gravity| {gravityMag:F3} — it would hold pieces " +
                        "aloft in free-space equilibrium, so the level never reaches rest and hangs into Timeout.");
                else if (upThrustExceedsGravity && zone.BypassHoverGuard)
                    warnings.Add(
                        $"AirZones[{i}] bypasses the hover-guard (up-flow Direction.Y·Acceleration = " +
                        $"{zone.Direction.Y * zone.Acceleration:F3} ≥ |gravity| {gravityMag:F3}); termination is NOT " +
                        "proven statically — the sim gate MUST show restReached, !timedOut, lostPieces=0 (§L(air) Rule 4).");

                // A zone that does not overlap Bounds does nothing (no released body can ever be inside it). Not a
                // structural break (an author may park a zone off-field intentionally), so WARNING only.
                if (!RectsOverlap(zone.Region, level.Bounds))
                    warnings.Add($"AirZones[{i}] region does not overlap Bounds — no piece can enter it.");
            }
        }

        /// <summary>AABB overlap test for two domain <see cref="Rect"/>s (touching edges count as overlap).</summary>
        private static bool RectsOverlap(Rect a, Rect b)
            => a.Min.X <= b.Max.X && a.Max.X >= b.Min.X && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;

        /// <summary>TASK-237: optional certified winning order check. Absent → OK (the hint is simply unavailable).
        /// When present, each placement index must be in <c>[0, Pieces.Count)</c>, reference a NON-preplaced (tapable)
        /// piece, and be unique — a preplaced/out-of-range/duplicate entry means the hint would point at a piece the
        /// player cannot tap, an authoring error (mirrors the unknown-primitive/physicsMode throws elsewhere).</summary>
        private static void ValidateWinningOrder(LevelData level, List<string> errors)
        {
            if (level.WinningOrder == null)
                return;

            var pieceCount = level.Pieces?.Count ?? 0;
            var seen = new HashSet<int>();
            foreach (var index in level.WinningOrder)
            {
                if (index < 0 || index >= pieceCount)
                {
                    errors.Add($"WinningOrder index {index} is out of range [0, {pieceCount}).");
                    continue;
                }
                if (!seen.Add(index))
                    errors.Add($"WinningOrder index {index} is listed more than once.");
                if (level.Pieces[index].Preplaced)
                    errors.Add($"WinningOrder index {index} references a preplaced piece — the hint can only point " +
                               "at a tapable piece.");
            }
        }

        /// <summary>TASK-374: optional co-release group check. Absent → OK (no groups). When present, each group is a
        /// set of placement indices that must be tapped together to fall (<see cref="LevelData.CoReleaseGroups"/>). A
        /// group must have ≥2 members (a group of one is an ordinary piece — declaring it would make it unreleasable, so
        /// it is an authoring error); every index must be in <c>[0, Pieces.Count)</c>, reference a NON-preplaced
        /// (tapable) piece (a preplaced piece spawns Falling and is never tapped, so it can never complete a group), and
        /// be UNIQUE across all groups (one piece belongs to at most one group — the domain maps index → single group).
        /// A group on an Assembly-mode level is technically supported (the rule is general, not seesaw-only) but unusual,
        /// so it is a WARNING, not an error. Mirrors the unknown-primitive / out-of-range-WinningOrder throw class.</summary>
        private static void ValidateCoReleaseGroups(LevelData level, List<string> errors, List<string> warnings)
        {
            if (level.CoReleaseGroups == null)
                return;

            var pieceCount = level.Pieces?.Count ?? 0;
            var seen = new HashSet<int>();
            for (var g = 0; g < level.CoReleaseGroups.Count; g++)
            {
                var group = level.CoReleaseGroups[g];
                if (group == null || group.Count < 2)
                {
                    errors.Add($"CoReleaseGroups[{g}] has {(group?.Count ?? 0)} members; a co-release group needs ≥2 " +
                               "(a group of one is an ordinary immediate-release piece — declaring it would make it " +
                               "unreleasable).");
                    continue;
                }

                foreach (var index in group)
                {
                    if (index < 0 || index >= pieceCount)
                    {
                        errors.Add($"CoReleaseGroups[{g}] index {index} is out of range [0, {pieceCount}).");
                        continue;
                    }
                    if (!seen.Add(index))
                        errors.Add($"CoReleaseGroups[{g}] index {index} is listed more than once (a piece may belong " +
                                   "to at most one co-release group).");
                    if (level.Pieces[index].Preplaced)
                        errors.Add($"CoReleaseGroups[{g}] index {index} references a preplaced piece — a co-release " +
                                   "group can only hold tapable pieces (a preplaced piece spawns Falling and is never " +
                                   "tapped, so the group could never complete).");
                }
            }

            if (level.CoReleaseGroups.Count > 0 && level.PhysicsMode == PhysicsMode.Assembly)
                warnings.Add("CoReleaseGroups are declared on an Assembly-mode level — supported (the rule is general), " +
                             "but co-release was designed for balance/catapult pairs; double-check this is intended.");
        }

        /// <summary>
        /// TASK-304: for every piece, its START collider (the shape at the authored start pose) must not overlap any
        /// <see cref="LevelData.StaticGeometry"/> block by more than <see cref="MinStaticPenetration"/>. A piece resting
        /// FLUSH on a static is legal (depth ≈ 0); only a real embed is an ERROR, named with the piece and the depth.
        ///
        /// FILLED SOLID block: the CANONICAL <see cref="Geometry.Penetration"/> over <see cref="ConvexShape.Of"/> — the
        /// SAME collider dispatch the physics adapter and the sim honesty detector use (native disc for a circle,
        /// compound for a concave polyomino, world-vertex polygon for every other primitive) — so this rule can never
        /// disagree with the sim on how a piece is shaped, and it covers ALL primitives for free. A filled solid is
        /// convex (ENFORCED by <see cref="ValidateStaticGeometry"/>'s TASK-307 gate) but of EITHER winding, so each
        /// outline is normalised to CCW before measuring (the circle-in-polygon inside-test is CCW-only).
        ///
        /// SURFACE block (TASK-325 open edge-chain chute/ramp) — TASK-329: measured against its ZERO-WIDTH edge-chain
        /// centreline, which is EXACTLY the <c>EdgeCollider2D</c> the sim builds from the outline
        /// (<c>UnityShapeFillSimulation</c>: <c>edge.points = ToPoints(polygon.Outline)</c>), NOT the ±½·thickness
        /// rendered ribbon. The old ribbon measure over-reported a piece resting flush on the centreline by exactly half
        /// the thickness (the piece touches the centreline yet sinks ½·thickness into the near ribbon half) — the false
        /// "starts embedded" of TASK-329, which authors worked around by pinning pieces flush to the ribbon EDGE. The
        /// centreline measure (<see cref="SurfaceEmbedDepth"/>) reads ~0 for a flush rest and the true half-depth for a
        /// piece threaded onto the film, on the SAME <see cref="MinStaticPenetration"/> scale as the filled branch — so
        /// no separate threshold is introduced (both are a world-unit embed depth) and the ribbon stays frozen (view +
        /// poseHash only).
        /// </summary>
        private static void ValidateStaticStartOverlap(LevelData level, List<string> errors)
        {
            if (level.Pieces == null || level.StaticGeometry == null)
                return;

            for (var g = 0; g < level.StaticGeometry.Length; g++)
            {
                var block = level.StaticGeometry[g];
                if (block?.Outline == null || !block.HasCollider)
                    continue;

                // SURFACE → zero-width edge-chain (TASK-329); FILLED → convex-collider SAT. Build the filled collider
                // once per block; the surface path measures the outline directly (no ribbon).
                var surface = block.IsSurface && block.Outline.Length >= 2;
                var filledShape = default(ConvexShape);
                if (!surface && !TryBuildStaticCollider(block, out filledShape))
                    continue;

                for (var i = 0; i < level.Pieces.Count; i++)
                {
                    var type = level.Pieces[i].Type;
                    if (type?.Shape == null)
                        continue; // a null type/shape is already reported by ValidatePieces — do not NRE here

                    var pieceShape = ConvexShape.Of(
                        type.Shape, level.Pieces[i].StartPosition, level.Pieces[i].StartRotation);
                    var pen = surface
                        ? SurfaceEmbedDepth(block.Outline, pieceShape)
                        : Geometry.Penetration(pieceShape, filledShape);
                    if (pen > MinStaticPenetration)
                        errors.Add(
                            $"Pieces[{i}] '{type.Kind}' starts embedded in StaticGeometry[{g}] — penetration " +
                            $"{pen:F3} world units at start pose {level.Pieces[i].StartPosition}. A piece may rest " +
                            $"flush on a surface (≤{MinStaticPenetration:0.###}) but must not begin inside it.");
                }
            }
        }

        /// <summary>
        /// TASK-628: every PREPLACED piece should start RESTING on what holds it, not hovering above it. A preplaced piece
        /// is released into physics at t=0 (<c>ShapeFillMechanicController.SpawnPreplaced</c>), so a gap under it is a
        /// visible drop at level start — furniture that "settles" before the player's first tap (owner playtest of
        /// potted_tree, 2026-10-19). Supports are colliding static blocks and the OTHER preplaced pieces at their start
        /// poses; tapable pieces hang outside physics until tapped, so they never hold anything at t=0.
        ///
        /// The measure is the straight-down drop to first contact (<see cref="PreplacedDrop"/>), on the same collider
        /// geometry as #11 (<see cref="ConvexShape.Of"/>, filled SAT / zero-width surface chain). Translation only — a
        /// piece that would tip while falling is not modelled; for the thing this guards (a hair of gap under a piece
        /// meant to sit) the first contact is what matters. A piece with nothing below it within Bounds is not reported
        /// (it falls by design or out of the level — not a rest question). WARNING, not error: a deliberate drop of a
        /// preplaced piece is legal design; the number lets the author tell a hair from an intent.
        /// </summary>
        private static void ValidatePreplacedRest(LevelData level, List<string> warnings)
        {
            if (level.Pieces == null)
                return;

            for (var i = 0; i < level.Pieces.Count; i++)
            {
                var piece = level.Pieces[i];
                if (!piece.Preplaced || piece.Type?.Shape == null)
                    continue;

                var drop = PreplacedDrop(level, i, out var support);
                // +∞ = nothing below within Bounds: not a rest question (see summary), so never a warning.
                if (drop > PreplacedRestTolerance && !float.IsPositiveInfinity(drop))
                    warnings.Add(
                        $"Preplaced rest: Pieces[{i}] '{piece.Type.Kind}' starts {drop:F3} world units above its support " +
                        $"{support} — it drops at level start. A preplaced piece meant to sit should start resting " +
                        $"(gap ≤{PreplacedRestTolerance:0.###}).");
            }
        }

        /// <summary>TASK-628: how far preplaced piece <paramref name="index"/> falls STRAIGHT DOWN from its start pose
        /// before first contact with a support (see <see cref="ValidatePreplacedRest"/>); 0 when it already touches,
        /// <see cref="float.PositiveInfinity"/> when nothing is below it within Bounds. March at
        /// <see cref="DropProbeStep"/>, then bisect the last free/contact interval — a fixed-step search instead of an
        /// analytic time-of-impact, because it reuses the exact penetration kernels #11 trusts for every primitive
        /// (disc, polygon, compound, surface chain) rather than adding a second geometry path that could disagree.</summary>
        public static float PreplacedDrop(LevelData level, int index, out string support)
        {
            support = null;
            var piece = level.Pieces[index];
            if (Contact(level, index, 0f, out support))
                return 0f;

            var floor = level.Bounds.Min.Y;
            var free = 0f;
            for (var d = DropProbeStep; piece.StartPosition.Y - d >= floor - DropProbeStep; d += DropProbeStep)
            {
                if (!Contact(level, index, d, out support))
                {
                    free = d;
                    continue;
                }

                var hit = d;
                for (var k = 0; k < 12; k++)
                {
                    var mid = 0.5f * (free + hit);
                    if (Contact(level, index, mid, out _))
                        hit = mid;
                    else
                        free = mid;
                }
                return hit;
            }

            support = null;
            return float.PositiveInfinity;
        }

        /// <summary>Does preplaced piece <paramref name="index"/>, lowered by <paramref name="drop"/>, overlap a support
        /// deeper than <see cref="MinStaticPenetration"/> (the same flush threshold as #11)? Names the support hit.</summary>
        private static bool Contact(LevelData level, int index, float drop, out string support)
        {
            var piece = level.Pieces[index];
            var shape = ConvexShape.Of(piece.Type.Shape,
                new Vec2(piece.StartPosition.X, piece.StartPosition.Y - drop), piece.StartRotation);

            if (level.StaticGeometry != null)
                for (var g = 0; g < level.StaticGeometry.Length; g++)
                {
                    var block = level.StaticGeometry[g];
                    if (block?.Outline == null || !block.HasCollider)
                        continue;
                    float pen;
                    if (block.IsSurface)
                        pen = block.Outline.Length >= 2 ? SurfaceEmbedDepth(block.Outline, shape) : 0f;
                    else if (TryBuildStaticCollider(block, out var filled))
                        pen = Geometry.Penetration(shape, filled);
                    else
                        continue;
                    if (pen > MinStaticPenetration)
                    {
                        support = $"StaticGeometry[{g}]";
                        return true;
                    }
                }

            for (var j = 0; j < level.Pieces.Count; j++)
            {
                var other = level.Pieces[j];
                if (j == index || !other.Preplaced || other.Type?.Shape == null)
                    continue;
                var otherShape = ConvexShape.Of(other.Type.Shape, other.StartPosition, other.StartRotation);
                if (Geometry.Penetration(shape, otherShape) > MinStaticPenetration)
                {
                    support = $"Pieces[{j}] '{other.Type.Kind}'";
                    return true;
                }
            }

            support = null;
            return false;
        }

        /// <summary>TASK-329: how deep the ZERO-WIDTH surface edge-chain reaches INSIDE a piece's convex collider at its
        /// start pose — the honest embed depth against the <c>EdgeCollider2D</c> the sim actually simulates, not the
        /// rendered ribbon. A piece resting FLUSH reads ~0 (the chain is tangent / lies on the boundary); a piece
        /// threaded onto the film reads the deepest the chain sinks into it. Max over the chain's segments (and over a
        /// concave piece's convex parts). Same world-unit scale as the filled-solid SAT, so <see cref="MinStaticPenetration"/>
        /// gates both.</summary>
        private static float SurfaceEmbedDepth(Vec2[] chain, in ConvexShape piece)
        {
            var max = 0f;
            for (var i = 0; i + 1 < chain.Length; i++)
            {
                var d = SegmentPieceDepth(chain[i], chain[i + 1], piece);
                if (d > max)
                    max = d;
            }
            return max;
        }

        /// <summary>The deepest a single edge-chain segment reaches inside one piece collider. A circle is exact
        /// (radius − distance from centre to the segment); a compound (concave polyomino) is the max over its convex
        /// parts; a convex polygon defers to <see cref="SegmentConvexDepth"/>.</summary>
        private static float SegmentPieceDepth(Vec2 a, Vec2 b, in ConvexShape piece)
        {
            if (piece.IsCircle)
                return System.MathF.Max(0f, piece.Radius - DistancePointToSegment(piece.Center, a, b));
            if (piece.IsCompound)
            {
                var max = 0f;
                for (var p = 0; p < piece.Parts.Length; p++)
                {
                    var d = SegmentConvexDepth(a, b, piece.Parts[p]);
                    if (d > max)
                        max = d;
                }
                return max;
            }
            return SegmentConvexDepth(a, b, piece.Vertices);
        }

        /// <summary>Distance from a point to a segment (segment-clamped, so endpoints are exact).</summary>
        private static float DistancePointToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var e = b - a;
            var ee = Vec2.Dot(e, e);
            var t = ee < 1e-12f ? 0f : Vec2.Dot(p - a, e) / ee;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return (p - (a + e * t)).Length;
        }

        /// <summary>The deepest a straight segment reaches INSIDE a convex polygon: the maximum, over the segment's
        /// interior, of the distance to the polygon boundary — 0 when the segment never enters the interior (a piece
        /// resting flush, its edge lying ON the chain, reads 0 because boundary points are 0 from the boundary). A
        /// zero-width segment cannot be measured by SAT (its own axis reports 0 overlap and falsely "separates"), so
        /// this measures the pierce depth directly. Distance-to-boundary along the segment is a concave, piecewise-
        /// linear function of the segment parameter, so its maximum is at an endpoint or where the two nearest edges
        /// tie; those candidate parameters are enumerated and the max over interior candidates is returned.</summary>
        private static float SegmentConvexDepth(Vec2 a, Vec2 b, Vec2[] verts)
        {
            if (verts == null || verts.Length < 3)
                return 0f;
            var n = verts.Length;
            var ab = b - a;

            // g_k(t) = c_k + d_k·t is the (unit) signed distance of the segment point a+t·ab to edge k's line; only |g|
            // is used (via the boundary-distance helper), so the normal's sign convention is irrelevant.
            var c = new float[n];
            var d = new float[n];
            for (var k = 0; k < n; k++)
            {
                var v0 = verts[k];
                var edge = verts[(k + 1) % n] - v0;
                var len = edge.Length;
                if (len < 1e-6f) { c[k] = float.NaN; continue; } // degenerate edge — skip as a tie source
                var nx = edge.Y / len;
                var ny = -edge.X / len;
                c[k] = (a.X - v0.X) * nx + (a.Y - v0.Y) * ny;
                d[k] = ab.X * nx + ab.Y * ny;
            }

            var cands = new List<float> { 0f, 1f };
            for (var i = 0; i < n; i++)
            {
                if (float.IsNaN(c[i]))
                    continue;
                for (var j = i + 1; j < n; j++)
                {
                    if (float.IsNaN(c[j]))
                        continue;
                    // |g_i| = |g_j| ⇒ g_i = g_j OR g_i = −g_j — the two possible ridge parameters.
                    AddRootInUnit(cands, d[i] - d[j], c[j] - c[i]);
                    AddRootInUnit(cands, d[i] + d[j], -(c[i] + c[j]));
                }
            }

            var best = 0f;
            for (var ci = 0; ci < cands.Count; ci++)
            {
                var t = cands[ci];
                if (t < 0f || t > 1f)
                    continue;
                var p = a + ab * t;
                if (!Geometry.PointInPolygon(verts, p))
                    continue;
                var dist = Geometry.DistanceToPolygonBoundary(verts, p);
                if (dist > best)
                    best = dist;
            }
            return best;
        }

        /// <summary>Add the root t of <c>m·t = rhs</c> to the candidate list when it lies in [0,1] and m is non-degenerate.</summary>
        private static void AddRootInUnit(List<float> cands, float m, float rhs)
        {
            if (System.MathF.Abs(m) < 1e-9f)
                return;
            var t = rhs / m;
            if (t >= 0f && t <= 1f)
                cands.Add(t);
        }

        /// <summary>
        /// TASK-332: no TARGET slot's CENTRE (its authored anchor point) may lie INSIDE a static-geometry collider — a
        /// slot whose centre is buried means the piece is ≥half-submerged in the floor/static and can never be assembled
        /// there, the exact "green gate, 0% winnable" the owner hit on fan_quilt and chute_cross (the floor cut through
        /// the LOWER HALF of the target — the lowest slots' centres fell inside the floor band).
        ///
        /// The test is on the CENTRE POINT, not on collider penetration, and that distinction is load-bearing: a target
        /// slot legitimately OVERLAPS a decorative static by a hair when the piece rests against/on it (locomotive's
        /// wheel embeds 0.038, petal_gully's petal 0.002 — both ship and are winnable). A penetration threshold cannot
        /// separate that shallow, reachable overlap from the deep, unreachable cut-through without conflating the two;
        /// the centre-in-static test does — it fires only once the anchor is buried (piece ≥50% inside), i.e. the "центр
        /// слота ≥ полу-высоты над опорой И не внутри статика" the ticket asks for. Winnability itself stays level-sim's
        /// job (this is the structural, deterministic half). Even-odd <see cref="Geometry.PointInPolygon"/> (winding-
        /// agnostic) on a filled block's outline, or on the rendered ribbon quads of a surface (same
        /// <see cref="Geometry.SurfaceRibbonQuads"/> the view and #11 use). A collider:false VISUAL-ONLY block is
        /// skipped (nothing to be buried in). NOTE (limitation): a slot floating in free space entirely BELOW a thin
        /// static it never overlaps is not caught — its centre is not inside anything; catching that would need a
        /// support/above heuristic, left out deliberately (it would risk false-failing a figure legitimately drawn above
        /// a decorative surface). The observed defect is a cut-THROUGH (centre buried), which this catches. See §D25.
        /// </summary>
        private static void ValidateSlotReachability(LevelData level, List<string> errors)
        {
            var slots = level.Target?.Slots;
            if (slots == null || slots.Count == 0 || level.StaticGeometry == null)
                return;

            for (var g = 0; g < level.StaticGeometry.Length; g++)
            {
                var block = level.StaticGeometry[g];
                // collider:false → VISUAL-ONLY (TASK-346); nothing to be buried in. Degenerate blocks are reported by
                // ValidateStaticGeometry and have no interior to test.
                if (block?.Outline == null || !block.HasCollider)
                    continue;

                for (var s = 0; s < slots.Count; s++)
                {
                    if (!StaticContainsPoint(block, slots[s].Position))
                        continue;
                    errors.Add(
                        $"Target slot[{s}] '{slots[s].Kind}' is UNREACHABLE — its target-pose CENTRE {slots[s].Position} " +
                        $"lies INSIDE StaticGeometry[{g}]; the piece would be ≥half-buried in the floor/static and can " +
                        "never be assembled there (the static cuts through the figure). A slot may rest flush ON a " +
                        "surface, but its centre must not lie inside static geometry.");
                }
            }
        }

        /// <summary>TASK-332: does a static-geometry block's INTERIOR contain a world point? A filled solid is its outline
        /// ring; a surface has no filled interior, so its "interior" is the rendered ribbon (any of the convex segment
        /// quads). Even-odd <see cref="Geometry.PointInPolygon"/> is winding-agnostic, so no CCW normalisation is needed.
        /// Caller has already excluded collider:false and null-outline blocks.</summary>
        private static bool StaticContainsPoint(Polygon block, Vec2 p)
        {
            if (block.IsSurface)
            {
                if (block.Outline.Length < 2)
                    return false;
                var quads = Geometry.SurfaceRibbonQuads(block.Outline, block.Surface.Thickness);
                for (var i = 0; i < quads.Length; i++)
                    if (Geometry.PointInPolygon(quads[i], p))
                        return true;
                return false;
            }

            return block.Outline.Length >= 3 && Geometry.PointInPolygon(block.Outline, p);
        }

        /// <summary>Build the convex collider the physics simulates for one FILLED static-geometry block, or return
        /// false when there is none to embed in. Called only by <see cref="ValidateStaticStartOverlap"/> for the filled
        /// path — a SURFACE is now measured against its zero-width edge-chain by <see cref="SurfaceEmbedDepth"/>
        /// (TASK-329), not a ribbon collider, so this no longer builds one for surfaces (returns false). A
        /// <c>collider:false</c> block is VISUAL-ONLY (TASK-346) — feeding its legally CONCAVE outline to the convex
        /// <see cref="ConvexShape.Polygon"/> would compute a bogus penetration and false-flag — so it yields no
        /// collider. A filled solid is normalised to CCW (the circle-in-polygon inside-test is CCW-only). Degenerate
        /// blocks (&lt;3 vertices) are the view's problem, reported by <see cref="ValidateStaticGeometry"/>, so they
        /// yield no collider here. (Slot-reachability, TASK-332, does its own even-odd containment in
        /// <see cref="StaticContainsPoint"/> and never calls this.)</summary>
        private static bool TryBuildStaticCollider(Polygon block, out ConvexShape staticShape)
        {
            staticShape = default;
            if (block?.Outline == null || !block.HasCollider || block.IsSurface)
                return false;
            if (block.Outline.Length < 3)
                return false; // no collider to embed in (degenerate blocks are the view's problem, not this rule's)
            staticShape = ConvexShape.Polygon(EnsureCcw(block.Outline));
            return true;
        }

        /// <summary>
        /// TASK-325 / TASK-307: structural validation of every <see cref="LevelData.StaticGeometry"/> block BEFORE its
        /// collider is built. Static geometry is a ONE-PATH collider (a <c>PolygonCollider2D</c> for a filled solid, an
        /// <c>EdgeCollider2D</c> chain for a surface), and the sim, the view and the start-embed check all assume it is
        /// a simple contour, so a malformed block must fail LOUD at load, not mis-simulate silently. Per block:
        ///  • VERTEX COUNT — a filled solid needs ≥3, an open surface ≥2 (below that there is no shape to build);
        ///  • DEGENERATE EDGES — a segment shorter than <see cref="MinStaticSegment"/> is a duplicated/zero-length point;
        ///  • SELF-INTERSECTION — a solid ring or a surface polyline that crosses itself is not a simple contour (the
        ///    precondition of Box2D's convex decomposition, of a clean edge chain, and of the ribbon triangulation);
        ///  • TASK-307 CONCAVITY GATE — a CONCAVE FILLED SOLID is rejected outright: Box2D splits its non-convex contour
        ///    by a COORDINATE-DEPENDENT split (the arm64↔editor divergence mechanism #2 of TASK-072), so a concave
        ///    static must be authored as an open SURFACE (edge chain — no decomposition, no split) or, later, as
        ///    authored convexParts (S3). A SURFACE is EXEMPT — an edge chain never reaches the split — which is exactly
        ///    why the S2 surface path closes TASK-307 by construction.
        /// Every current corpus static is a small convex filled block, so this rejects nothing today; it gates the new
        /// concave/curved static authoring the task enables.
        /// </summary>
        private static void ValidateStaticGeometry(LevelData level, List<string> errors)
        {
            if (level.StaticGeometry == null)
                return;

            for (var g = 0; g < level.StaticGeometry.Length; g++)
            {
                var block = level.StaticGeometry[g];
                if (block?.Outline == null)
                    continue;

                var outline = block.Outline;
                var isSurface = block.IsSurface;
                var minVerts = isSurface ? 2 : 3;
                if (outline.Length < minVerts)
                {
                    errors.Add($"StaticGeometry[{g}] has {outline.Length} vertices; a " +
                               $"{(isSurface ? "surface polyline needs ≥2" : "filled block needs ≥3")}.");
                    continue;
                }

                // Degenerate edges: a duplicated point / zero-length segment. A surface is an OPEN polyline
                // (outline.Length−1 segments); a filled solid is a CLOSED ring (the last vertex wraps to the first).
                var segCount = isSurface ? outline.Length - 1 : outline.Length;
                for (var i = 0; i < segCount; i++)
                {
                    var next = (i + 1) % outline.Length;
                    if ((outline[next] - outline[i]).Length < MinStaticSegment)
                        errors.Add($"StaticGeometry[{g}] edge {i}→{next} is degenerate (length < " +
                                   $"{MinStaticSegment:0.####}) — a duplicated or zero-length point.");
                }

                // Self-intersection: a simple contour is the precondition of every downstream consumer.
                if (SelfIntersects(outline, closed: !isSurface))
                    errors.Add($"StaticGeometry[{g}] self-intersects — a static " +
                               $"{(isSurface ? "surface polyline" : "block ring")} must be simple (no crossing edges).");

                // TASK-307: a concave FILLED solid would be split coordinate-dependently by Box2D (platform-dependent
                // physics). Surfaces are exempt (edge chain — no split). TASK-346: a collider:false block is exempt too —
                // it builds NO collider (a VISUAL-ONLY static, rendered by the concave triangulator), so there is no
                // Box2D split to diverge; concavity is legal for it. The degenerate-edge and self-intersection checks
                // above still run on it (the triangulator needs a simple polygon).
                if (!isSurface && block.HasCollider && outline.Length >= 3 && !Geometry.IsConvex(outline))
                    errors.Add($"StaticGeometry[{g}] is a CONCAVE filled solid; Box2D would split it by a " +
                               "coordinate-dependent split (platform-dependent physics, TASK-072/307). Author a concave " +
                               "ramp/chute as a surface (surface:true — an EdgeCollider2D chain, no split) or as " +
                               "authored convexParts.");
            }
        }

        /// <summary>TASK-325: does a static contour cross itself? Tests every pair of NON-ADJACENT segments for a proper
        /// crossing (interiors intersect). <paramref name="closed"/> ⇒ a filled ring (wraps the last→first edge and
        /// treats first/last edges as adjacent); false ⇒ an open surface polyline. O(n²), fine for the handful of
        /// vertices a static carries. Only PROPER crossings are flagged — collinear touches at shared endpoints are
        /// legal (adjacent segments always share a vertex) and are excluded, so a straight polyline never false-trips.</summary>
        private static bool SelfIntersects(Vec2[] pts, bool closed)
        {
            var n = pts.Length;
            var segCount = closed ? n : n - 1;
            for (var i = 0; i < segCount; i++)
            {
                var a1 = pts[i];
                var a2 = pts[(i + 1) % n];
                for (var j = i + 2; j < segCount; j++)
                {
                    if (closed && i == 0 && j == segCount - 1)
                        continue; // first and last ring edges share vertex 0 — adjacent, not a crossing
                    if (SegmentsProperlyIntersect(a1, a2, pts[j], pts[(j + 1) % n]))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Proper segment intersection: the two segments' interiors cross (each straddles the other's line).
        /// Shared endpoints and collinear overlaps return false — <see cref="SelfIntersects"/> already excludes adjacent
        /// segments, so only a genuine self-crossing survives.</summary>
        private static bool SegmentsProperlyIntersect(Vec2 p1, Vec2 p2, Vec2 p3, Vec2 p4)
        {
            var d1 = Orient(p3, p4, p1);
            var d2 = Orient(p3, p4, p2);
            var d3 = Orient(p1, p2, p3);
            var d4 = Orient(p1, p2, p4);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        /// <summary>2D cross product (a→b) × (a→c): &gt;0 c is left of a→b, &lt;0 right, 0 collinear.</summary>
        private static float Orient(Vec2 a, Vec2 b, Vec2 c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        /// <summary>Return <paramref name="outline"/> wound counter-clockwise (positive signed area): reverses a CW
        /// ring, leaves a CCW one untouched. Static outlines are authored either winding, but the circle-in-polygon
        /// inside-test in <see cref="Geometry"/> is documented for CONVEX CCW polygons — feeding a CW ring would let a
        /// circle centre deep inside a block read as separated. Piece colliders are already CCW (the shape factory).</summary>
        private static Vec2[] EnsureCcw(Vec2[] outline)
        {
            var twiceArea = 0f;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                twiceArea += outline[j].X * outline[i].Y - outline[i].X * outline[j].Y;
            if (twiceArea >= 0f)
                return outline; // already CCW

            var reversed = new Vec2[outline.Length];
            for (var i = 0; i < outline.Length; i++)
                reversed[i] = outline[outline.Length - 1 - i];
            return reversed;
        }

        private static void ValidateTarget(LevelData level, List<string> errors)
        {
            if (level.Target == null)
            {
                errors.Add("Target figure is null.");
                return;
            }
            if (level.Target.Outline?.Outline == null || level.Target.Outline.Outline.Length < 3)
                errors.Add("Target outline must have at least 3 vertices.");
            if (level.Target.Slots == null || level.Target.Slots.Count == 0)
                errors.Add("Target has no slots.");
            // D14 (TASK-118, owner directive 2026-08-06 "одна фигура — это не прикольно"): a target figure must be at
            // least TWO slots — a single slot is "one piece in a pocket", not a readable silhouette. The count is on
            // target.slots (the REQUIRED figure pieces), NOT total pieces: an expendable tool (D11) or preplaced
            // furniture (§L11) is not part of the target and does not count toward the minimum. Hard ERROR, no phase-in
            // (D12): the corpus is clean at rule introduction (lever_lid, the sole violator, is fixed in this task), so
            // failing load on a 1-slot level creates no debt. See level-authoring §D14.
            else if (level.Target.Slots.Count < 2)
                errors.Add($"Target has {level.Target.Slots.Count} slot(s); a target figure needs at least 2 " +
                           "(D14: одна фигура — это не прикольно — a single slot is not a readable silhouette).");
        }

        private static void ValidatePieces(LevelData level, List<string> errors)
        {
            if (level.Pieces == null || level.Pieces.Count == 0)
            {
                errors.Add("Pieces list is empty.");
                return;
            }

            for (var i = 0; i < level.Pieces.Count; i++)
            {
                var type = level.Pieces[i].Type;
                if (type == null)
                {
                    errors.Add($"Pieces[{i}] has a null type.");
                    continue;
                }
                if (type.Shape?.Vertices == null || type.Shape.Vertices.Length < 3)
                    errors.Add($"Pieces[{i}] '{type.Kind}' has an invalid shape (need ≥3 vertices).");
                // TASK-074: a polygon collider with >8 vertices exceeds Box2D's per-shape convex cap, so the engine
                // re-splits it into convex sub-shapes by a split that depends on the exact vertex coordinates — a
                // platform-dependent physics input (Mono editor vs IL2CPP arm64), the P0 mechanism of TASK-072.
                // Circle is exempt: it maps to a NATIVE CircleCollider2D (UnityShapeFillSimulation:95), so its
                // vertices never reach a collider. Every other primitive gets a PolygonCollider2D built from these
                // vertices, so the rule keys on the RESULT (vertex count), not the primitive — it also catches a
                // future QuarterDisc or concave polyomino authored past 8 verts. Fix bad data (coarser arc / native
                // primitive), never widen this cap.
                else if (type.Shape.ConvexParts == null && type.Shape.Primitive != ShapePrimitive.Circle
                                                        && type.Shape.Vertices.Length > 8)
                    errors.Add($"Pieces[{i}] '{type.Kind}' has a {type.Shape.Vertices.Length}-vertex polygon collider; " +
                               "Box2D splits any >8-vertex polygon by a coordinate-dependent split (platform-dependent " +
                               "physics, TASK-072/074) — use a native primitive or ≤8 vertices.");
                // TASK-178: a concave polyomino is a COMPOUND — its collider is the authored ConvexParts, not the
                // concave contour, so it never reaches Box2D's coordinate-dependent split. Validate the decomposition
                // instead: every part must be a small convex polygon (≤8 verts, so Box2D takes it whole) and the parts
                // must cover the silhouette (area sum ≈ contour area). A bad decomposition is authoring garbage and is
                // flagged LOUD here rather than silently mis-simulating.
                if (type.Shape?.ConvexParts != null)
                    ValidateConvexParts(i, type, errors);
                // TASK-360: a polyomino contour is a trusted integer factory output, but a freeform contour is AUTHORED
                // data — judge its well-formedness (simple, positive area, CCW) like a static contour.
                if (type.Shape?.Primitive == ShapePrimitive.Freeform)
                    ValidateFreeformContour(i, type, errors);
                if (type.Material.Density < 0f || type.Material.Friction < 0f || type.Material.Restitution < 0f)
                    errors.Add($"Pieces[{i}] '{type.Kind}' has a negative material field.");
                if (!level.Bounds.Contains(level.Pieces[i].StartPosition))
                    errors.Add($"Pieces[{i}] '{type.Kind}' start position is outside Bounds.");
            }
        }

        /// <summary>TASK-178: structural check of a concave polyomino's authored convex decomposition — each part is a
        /// small convex polygon (Box2D takes ≤8-vertex convex whole), and the parts' total area matches the silhouette
        /// (so the compound collider covers the drawn shape). Not a geometry-exact union proof — enough to catch a
        /// mis-authored decomposition (missing/duplicated/spilled part), the way an unknown primitive is caught.</summary>
        private static void ValidateConvexParts(int index, PieceType type, List<string> errors)
        {
            var parts = type.Shape.ConvexParts;
            var partsArea = 0f;
            for (var p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part == null || part.Length < 3)
                {
                    errors.Add($"Pieces[{index}] '{type.Kind}' ConvexParts[{p}] has <3 vertices.");
                    continue;
                }
                if (part.Length > 8)
                    errors.Add($"Pieces[{index}] '{type.Kind}' ConvexParts[{p}] has {part.Length} vertices; a compound " +
                               "part must be ≤8 (Box2D splits a larger convex polygon coordinate-dependently, TASK-072).");
                if (!Geometry.IsConvex(part))
                    errors.Add($"Pieces[{index}] '{type.Kind}' ConvexParts[{p}] is not convex; each part must be convex " +
                               "so Box2D takes it whole (a concave part is exactly the coordinate-dependent split TASK-072 removes).");
                partsArea += PolygonArea(part);
            }

            var silhouetteArea = PolygonArea(type.Shape.Vertices);
            // ε relative to the silhouette: parts that overlap or leave gaps change the total area; exact tiling matches.
            if (System.MathF.Abs(partsArea - silhouetteArea) > 1e-3f * System.MathF.Max(silhouetteArea, 1e-4f))
                errors.Add($"Pieces[{index}] '{type.Kind}' ConvexParts cover area {partsArea:0.####} but the silhouette " +
                           $"is {silhouetteArea:0.####} — the decomposition has gaps or overlaps (must tile the contour).");
        }

        /// <summary>Absolute shoelace area of a simple polygon.</summary>
        private static float PolygonArea(Vec2[] v)
        {
            var sum = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                sum += v[j].X * v[i].Y - v[i].X * v[j].Y;
            return System.MathF.Abs(sum) * 0.5f;
        }

        /// <summary>Signed shoelace area of a polygon: &gt;0 CCW, &lt;0 CW.</summary>
        private static float SignedPolygonArea(Vec2[] v)
        {
            var twice = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                twice += v[j].X * v[i].Y - v[i].X * v[j].Y;
            return twice * 0.5f;
        }

        /// <summary>TASK-360: well-formedness of a FREEFORM piece's authored contour (its silhouette outline). A
        /// polyomino contour is a trusted integer factory output, but a freeform contour is authored data, so it is
        /// judged like a static contour: ≥3 vertices, positive area, simple (no self-intersection), and wound CCW. The
        /// tool emits CCW; the validator is the last line — a degenerate/self-crossing/CW contour is authoring garbage,
        /// rejected LOUD with a named cause rather than silently normalised ("молчаливо кривой контур хуже отказа"). The
        /// part-coverage invariant (Σ part area == contour area) and part convexity are already enforced by
        /// <see cref="ValidateConvexParts"/> (shared with the polyominoes), so they are not repeated here.</summary>
        private static void ValidateFreeformContour(int index, PieceType type, List<string> errors)
        {
            var outline = type.Shape?.Vertices;
            if (outline == null || outline.Length < 3)
                return; // the generic ≥3-vertex check in ValidatePieces already reported this
            if (PolygonArea(outline) <= 1e-6f)
                errors.Add($"Pieces[{index}] '{type.Kind}' freeform contour has non-positive area — a degenerate silhouette.");
            if (SelfIntersects(outline, closed: true))
                errors.Add($"Pieces[{index}] '{type.Kind}' freeform contour self-intersects — the silhouette must be a " +
                           "simple polygon (no crossing edges).");
            if (SignedPolygonArea(outline) < 0f)
                errors.Add($"Pieces[{index}] '{type.Kind}' freeform contour is wound clockwise — author it " +
                           "counter-clockwise (the decompose-shape tool emits CCW).");
        }

        private static void ValidateKindCoverage(LevelData level, List<string> errors)
        {
            if (level.Target?.Slots == null || level.Pieces == null)
                return;

            var slotCounts = new Dictionary<string, int>();
            foreach (var slot in level.Target.Slots)
                slotCounts[slot.Kind] = slotCounts.TryGetValue(slot.Kind, out var c) ? c + 1 : 1;

            var pieceCounts = new Dictionary<string, int>();
            foreach (var placement in level.Pieces)
            {
                // TASK-054: an expendable piece is a tool that may leave the field by design, so it does NOT
                // guarantee a slot can be filled — count only ESSENTIAL pieces toward coverage. Back-compat:
                // levels with no expendable piece count every piece, exactly as before.
                if (placement.Expendable)
                    continue;
                var kind = placement.Type?.Kind;
                if (kind == null)
                    continue;
                pieceCounts[kind] = pieceCounts.TryGetValue(kind, out var c) ? c + 1 : 1;
            }

            foreach (var kv in slotCounts)
            {
                pieceCounts.TryGetValue(kv.Key, out var have);
                if (have < kv.Value)
                    errors.Add($"Kind '{kv.Key}': {kv.Value} slot(s) but only {have} ESSENTIAL piece(s) — unwinnable.");
            }
        }

        /// <summary>
        /// TASK-318: the target-mosaic tiling gate. It engages ONLY on a level that is unambiguously a grid-cell
        /// mosaic and, when it does, requires the union of the target pieces in their AUTHORED POSES to be an honest
        /// tiling — no overlaps, no interior holes, cells summing to distinct.
        ///
        /// Applicability (all three ⇒ engage; otherwise SKIP silently — the rule is meaningless off a grid): the level
        /// is <see cref="PhysicsMode.Assembly"/>; at least one target slot is a POLYOMINO (a tetromino/pentomino, i.e.
        /// <see cref="Shape.ConvexParts"/> ≠ null) — which both declares "this is a cell grid" and fixes the cell
        /// pitch; and EVERY target slot is a grid-family piece (polyomino / box / plank). A single circle/triangle/…
        /// slot (chute_cross's ball) or the absence of any polyomino (a box-only figure or gapped pyramid like
        /// toy_pyramid/fan_quilt/robot/mailbox) means the level is not a grid mosaic and is skipped. This is the SAME
        /// applicability the owner's shipped reference tool (.studio/tools/level-tiling.py) uses — it only engages a
        /// level whose target carries a polyomino to fix the cell — so the two agree on all 27 corpus levels: only
        /// mosaic_bloom engages (and fails), every other level skips. See §3 of the RFC and the handoff note on the
        /// toy_pyramid deviation (the RFC's dims-only stage-B would have false-failed toy_pyramid's half-cell rows).
        ///
        /// The grid ORIGIN is inferred from the data (median cell phase, robust to a stray violator), never pinned to
        /// world-zero, so a piece with even width centred on a cell boundary is not mistaken for off-grid. Overlaps,
        /// interior holes (flood-fill from the padded frame, so external concavity — a cross/letter/flower — is legal),
        /// off-grid cells, and a piece-count-vs-distinct mismatch each become a load ERROR naming the offending cells.
        /// </summary>
        /// <summary>TASK-360: is this shape a GRID polyomino — an axis-aligned unit-cell tetromino/pentomino whose
        /// <see cref="Shape.ConvexParts"/> ARE the grid cells that fix the mosaic pitch? A <c>freeform</c> also carries
        /// ConvexParts, but they are arbitrary convex fragments, NOT grid cells, so it must neither DRIVE the mosaic
        /// pitch nor be measured against it — hence the mosaic gate keys its "grid family" test on THIS predicate, not
        /// on the raw <c>ConvexParts != null</c> that both share. This is the single point where a freeform silhouette
        /// is excluded from grid-mosaic tiling (a burger is not a решётчатая mosaic), exactly as a circle/triangle slot
        /// already is.</summary>
        private static bool IsGridPolyomino(Shape s) =>
            s.ConvexParts != null && s.Primitive != ShapePrimitive.Freeform;

        private static void ValidateTargetMosaic(LevelData level, List<string> errors)
        {
            if (level.PhysicsMode != PhysicsMode.Assembly)
                return;
            var slots = level.Target?.Slots;
            if (slots == null || slots.Count == 0)
                return;

            // kind → shape (first piece of each kind; missing coverage is ValidateKindCoverage's error, not ours).
            var kindShape = new Dictionary<string, Shape>();
            if (level.Pieces != null)
                foreach (var placement in level.Pieces)
                {
                    var t = placement.Type;
                    if (t?.Kind == null || t.Shape == null || kindShape.ContainsKey(t.Kind))
                        continue;
                    kindShape[t.Kind] = t.Shape;
                }

            // Applicability: a polyomino must fix the pitch AND every slot must be grid-family, else this is not a
            // grid mosaic and the rule does not apply.
            var anyPolyomino = false;
            var pitch = 0f;
            foreach (var slot in slots)
            {
                if (!kindShape.TryGetValue(slot.Kind, out var shape))
                    return; // a slot with no piece — the level is already broken (coverage); do not mosaic-check it.
                if (IsGridPolyomino(shape))
                {
                    if (!anyPolyomino)
                    {
                        pitch = PolyominoCellSide(shape);
                        anyPolyomino = true;
                    }
                }
                else if (shape.Primitive != ShapePrimitive.Box && shape.Primitive != ShapePrimitive.Plank)
                {
                    return; // a non-grid primitive (circle/triangle/…) in the target — not a grid mosaic (chute_cross).
                }
            }
            if (!anyPolyomino || pitch <= 0f)
                return; // no polyomino declared the grid (box-only figure / gapped pyramid) — rule N/A.

            // Build the occupancy of the union in world cell coordinates. Fractional cell coords first (wx/pitch − 0.5
            // puts a cell CENTRE on an integer), so the grid phase can be recovered as the median fractional part.
            var raw = new List<(string Kind, float Fx, float Fy)>();
            var totalCells = 0;
            foreach (var slot in slots)
            {
                var shape = kindShape[slot.Kind];

                // A grid mosaic tiles only at right angles: the rotation must be a multiple of 90°.
                var quarters = System.MathF.Round(slot.Rotation / (System.MathF.PI * 0.5f));
                var snappedRot = quarters * (System.MathF.PI * 0.5f);
                if (System.MathF.Abs(slot.Rotation - snappedRot) > GridRotationEpsilon)
                {
                    errors.Add($"Target mosaic '{level.LevelId}': slot '{slot.Kind}' rotation {slot.Rotation:F3} rad " +
                               "is not a multiple of 90° — a grid mosaic tiles only at right angles.");
                    continue;
                }

                if (!TryLocalCells(shape, pitch, out var localCells))
                {
                    errors.Add($"Target mosaic '{level.LevelId}': piece '{slot.Kind}' does not fit the {pitch:F3} grid " +
                               "pitch — its size is not an integer number of cells, so it cannot tile the target.");
                    continue;
                }

                var cos = System.MathF.Cos(snappedRot);
                var sin = System.MathF.Sin(snappedRot);
                foreach (var c in localCells)
                {
                    var wx = slot.Position.X + (c.X * cos - c.Y * sin);
                    var wy = slot.Position.Y + (c.X * sin + c.Y * cos);
                    raw.Add((slot.Kind, wx / pitch - 0.5f, wy / pitch - 0.5f));
                    totalCells++;
                }
            }
            if (raw.Count == 0)
                return;

            var originX = MedianFraction(raw, axisX: true);
            var originY = MedianFraction(raw, axisX: false);

            var occupancy = new Dictionary<(int Col, int Row), List<string>>();
            foreach (var (kind, fx, fy) in raw)
            {
                var col = (int)System.MathF.Round(fx - originX);
                var row = (int)System.MathF.Round(fy - originY);
                if (System.MathF.Abs(fx - originX - col) > GridSnapEpsilon ||
                    System.MathF.Abs(fy - originY - row) > GridSnapEpsilon)
                    errors.Add($"Target mosaic '{level.LevelId}': piece '{kind}' has a cell off the grid at " +
                               $"({fx - originX:F2},{fy - originY:F2}) cells — not aligned to the {pitch:F3} pitch.");
                if (!occupancy.TryGetValue((col, row), out var claimants))
                {
                    claimants = new List<string>();
                    occupancy[(col, row)] = claimants;
                }
                claimants.Add(kind);
            }

            // 1. Overlaps: a cell claimed by more than one piece.
            var overlaps = 0;
            foreach (var kv in occupancy)
            {
                if (kv.Value.Count <= 1)
                    continue;
                overlaps++;
                if (overlaps <= MaxReportedMosaicCells)
                    errors.Add($"Target mosaic '{level.LevelId}': cell ({kv.Key.Col},{kv.Key.Row}) is claimed by " +
                               $"{kv.Value.Count} pieces [{string.Join(", ", kv.Value)}] — pieces overlap.");
            }
            if (overlaps > MaxReportedMosaicCells)
                errors.Add($"Target mosaic '{level.LevelId}': …and {overlaps - MaxReportedMosaicCells} more overlapped " +
                           $"cell(s) ({overlaps} total).");

            // 2. Interior holes: an empty cell enclosed by the silhouette (external concavity is legal).
            var holes = FindInteriorHoles(occupancy.Keys);
            for (var i = 0; i < holes.Count && i < MaxReportedMosaicCells; i++)
                errors.Add($"Target mosaic '{level.LevelId}': cell ({holes[i].Col},{holes[i].Row}) is an interior hole " +
                           "— enclosed by filled cells, no piece covers it.");
            if (holes.Count > MaxReportedMosaicCells)
                errors.Add($"Target mosaic '{level.LevelId}': …and {holes.Count - MaxReportedMosaicCells} more interior " +
                           $"hole(s) ({holes.Count} total).");

            // 3. Sum: distinct occupied cells must equal the total cells contributed by all pieces.
            if (occupancy.Count != totalCells)
                errors.Add($"Target mosaic '{level.LevelId}': pieces cover {totalCells} cells but only {occupancy.Count} " +
                           "are distinct — the union is not a gap-free tiling (see the overlaps above).");
        }

        /// <summary>TASK-318: the world size of one grid cell for a polyomino, read as the edge length of its first
        /// authored convex part (each part is a unit square scaled by the authored <c>cell</c>, <see cref="Shape"/>).</summary>
        private static float PolyominoCellSide(Shape shape)
        {
            var part = shape.ConvexParts[0];
            var dx = part[1].X - part[0].X;
            var dy = part[1].Y - part[0].Y;
            return System.MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>TASK-318: the LOCAL-space cell centres of a grid-family piece (before its slot pose). A polyomino
        /// yields one centre per authored convex part (exact). A box/plank is split into <c>round(w/pitch)×round(h/pitch)</c>
        /// cells centred on the piece origin; returns false if the piece is smaller than a cell or its size is not an
        /// integer number of cells (<see cref="GridMultipleEpsilon"/>) — it cannot tile the grid.</summary>
        private static bool TryLocalCells(Shape shape, float pitch, out List<Vec2> cells)
        {
            cells = new List<Vec2>();
            if (IsGridPolyomino(shape))
            {
                foreach (var part in shape.ConvexParts)
                {
                    float cx = 0f, cy = 0f;
                    for (var i = 0; i < part.Length; i++)
                    {
                        cx += part[i].X;
                        cy += part[i].Y;
                    }
                    cells.Add(new Vec2(cx / part.Length, cy / part.Length));
                }
                return true;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in shape.Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            var nxF = (maxX - minX) / pitch;
            var nyF = (maxY - minY) / pitch;
            var nx = (int)System.MathF.Round(nxF);
            var ny = (int)System.MathF.Round(nyF);
            if (nx < 1 || ny < 1)
                return false;
            if (System.MathF.Abs(nxF - nx) > GridMultipleEpsilon || System.MathF.Abs(nyF - ny) > GridMultipleEpsilon)
                return false;
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < ny; j++)
                    cells.Add(new Vec2((i - (nx - 1) * 0.5f) * pitch, (j - (ny - 1) * 0.5f) * pitch));
            return true;
        }

        /// <summary>TASK-318: the median fractional part of the raw cell coordinates on one axis — the grid phase. The
        /// median is robust to a stray off-grid violator (a single misplaced piece cannot drag the whole grid), unlike
        /// anchoring on the first cell.</summary>
        private static float MedianFraction(List<(string Kind, float Fx, float Fy)> raw, bool axisX)
        {
            var fracs = new List<float>(raw.Count);
            foreach (var r in raw)
            {
                var v = axisX ? r.Fx : r.Fy;
                fracs.Add(v - System.MathF.Floor(v));
            }
            fracs.Sort();
            return fracs[fracs.Count / 2];
        }

        /// <summary>TASK-318: empty cells enclosed by the silhouette — a flood-fill of the padded bounding box from a
        /// corner (guaranteed outside the figure) over empty cells; any empty cell the fill cannot reach is walled in
        /// by filled cells and is a real hole. External concavity (a cross's notches, a letter's bays) is reachable
        /// from the frame and is legal, exactly as the owner's reference tool defines it.</summary>
        private static List<(int Col, int Row)> FindInteriorHoles(ICollection<(int Col, int Row)> occupied)
        {
            var filled = new HashSet<(int, int)>(occupied);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (c, r) in filled)
            {
                if (c < minX) minX = c;
                if (c > maxX) maxX = c;
                if (r < minY) minY = r;
                if (r > maxY) maxY = r;
            }
            int x0 = minX - 1, x1 = maxX + 1, y0 = minY - 1, y1 = maxY + 1;

            var reached = new HashSet<(int, int)> { (x0, y0) };
            var queue = new Queue<(int, int)>();
            queue.Enqueue((x0, y0));
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < x0 || nx > x1 || ny < y0 || ny > y1)
                        continue;
                    if (reached.Contains((nx, ny)) || filled.Contains((nx, ny)))
                        continue;
                    reached.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            var holes = new List<(int Col, int Row)>();
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    if (!filled.Contains((x, y)) && !reached.Contains((x, y)))
                        holes.Add((x, y));
            return holes;
        }
    }
}
