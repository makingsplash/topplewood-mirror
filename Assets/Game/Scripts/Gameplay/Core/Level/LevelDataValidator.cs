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

        /// <summary>TASK-340 phase gate. <c>false</c> ⇒ a symmetryOrder/shape mismatch loads as a WARNING (phase 1: the
        /// corpus carries ~24 non-square boxes authored order 4, so a hard ERROR would brick ~11 levels at load). Flip
        /// to <c>true</c> (phase 2) once the corpus-cleanup task has re-authored every violator — see the TASK-340
        /// tech-debt trigger. Flipping is a one-line change tracked by the same unit test (it asserts routing against
        /// THIS flag). Widening the RULE (accepting a non-dividing order) is never correct; this flag only moves an
        /// already-detected mismatch between WARNING and ERROR. Mirrors the canon-blessed TASK-085 tap-spacing rollout
        /// (<see cref="TapZoneConfig.EnforceMinDistanceAsError"/>), which phased in for the same reason.</summary>
        public const bool SymmetryOrderMismatchIsError = false;

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

            return new ValidationResult(errors, warnings);
        }

        /// <summary>
        /// TASK-085 (owner directive 2026-08-04): tapable pieces must not start closer than the minimum tap zone, or
        /// their touch targets merge and small pieces become unhittable. The zone is a SCREEN size (dp) turned into
        /// world units via the level's PREDICTED orthoSize (<see cref="CameraFramingMath"/> replayed on the reference
        /// device) — the same framing the live camera applies — so the check tracks what the player actually sees. The
        /// guard is on START poses of non-<c>Preplaced</c> pieces (the only tapable ones; preplaced pieces are Dynamic
        /// furniture, never tapped). Metric v1 = centre-to-centre (StartPosition): exact for the compact pieces that
        /// are actually the problem (horn/head/ear); elongated planks are large and never a tap problem (edge-to-edge
        /// is the tech-debt refinement, TASK-087).
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
                return new TapSpacingAudit(level?.LevelId, 0f, 0f, null);
            if (!CameraFramingMath.TryContentBounds(level, out var min, out var max))
                return new TapSpacingAudit(level.LevelId, 0f, 0f, null);

            var orthoSize = CameraFramingMath.ComputeOrthoSize(
                min, max, _tapZone.ReferenceAspect,
                _tapZone.RefMargin, _tapZone.RefPreviewBand, _tapZone.RefMinOrthoSize);
            var minWorld = _tapZone.MinTapDp * _tapZone.RefKDevice * orthoSize; // tap-zone DIAMETER in world units

            var violations = new List<TapSpacingViolation>();
            for (var i = 0; i < level.Pieces.Count; i++)
            {
                if (level.Pieces[i].Preplaced)
                    continue;
                for (var j = i + 1; j < level.Pieces.Count; j++)
                {
                    if (level.Pieces[j].Preplaced)
                        continue;
                    var dist = (level.Pieces[i].StartPosition - level.Pieces[j].StartPosition).Length;
                    if (dist >= minWorld)
                        continue;
                    violations.Add(new TapSpacingViolation(
                        i, level.Pieces[i].Type?.Kind, j, level.Pieces[j].Type?.Kind, dist));
                }
            }
            return new TapSpacingAudit(level.LevelId, minWorld, orthoSize, violations);
        }

        /// <summary>
        /// Phase-1/2 policy wrapper around <see cref="AuditTapSpacing"/>: formats each structural violation into the
        /// author-facing message and routes it to <paramref name="errors"/> or <paramref name="warnings"/> per
        /// <see cref="TapZoneConfig.EnforceMinDistanceAsError"/> (false = non-blocking WARNING, this task's phase 1;
        /// true = hard ERROR, phase 2 after the corpus is cleaned). The message text is preserved byte-for-byte from
        /// TASK-085 so load-time strings and any snapshot of them are unchanged.
        /// </summary>
        private void ValidateTapSpacing(LevelData level, List<string> errors, List<string> warnings)
        {
            var audit = AuditTapSpacing(level);
            foreach (var v in audit.Violations)
            {
                var msg =
                    $"Tap spacing: Pieces[{v.IndexA}] '{v.KindA}' and Pieces[{v.IndexB}] " +
                    $"'{v.KindB}' start {v.Distance:F3} apart — closer than the {audit.MinTapWorld:F3} world " +
                    $"tap zone ({_tapZone.MinTapDp}dp @ orthoSize {audit.OrthoSize:F2}); their tap targets merge.";
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

            // The phase flag is a compile-time const; copy it to a local so the (phase-2) ERROR branch below does not
            // read as unreachable dead code while the const is false. Flipping the const is then the only phase-2 change.
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
                default:
                    return 1;
            }
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

        /// <summary>
        /// TASK-304: for every piece, its START collider (the shape at the authored start pose) must not overlap any
        /// <see cref="LevelData.StaticGeometry"/> block by more than <see cref="MinStaticPenetration"/>. The measure is
        /// the CANONICAL <see cref="Geometry.Penetration"/> over <see cref="ConvexShape.Of"/> — the SAME collider
        /// dispatch the physics adapter and the sim honesty detector use (native disc for a circle, compound for a
        /// concave polyomino, world-vertex polygon for every other primitive) — so this rule can never disagree with
        /// the sim on how a piece is shaped, and it covers ALL primitives (circle/box/plank/trapezoid/triangle/…) for
        /// free. A piece resting FLUSH on a surface is legal (penetration ≈ 0); only a real embed is an ERROR, named
        /// with the piece and the depth. A FILLED SOLID block is convex (now ENFORCED, not merely assumed, by
        /// <see cref="ValidateStaticGeometry"/>'s TASK-307 gate) but of EITHER winding, so each outline is normalised
        /// to CCW before measuring — <see cref="Geometry"/>'s circle-in-polygon inside-test is CCW-only and the ramp on
        /// petal_gully is authored clockwise. A SURFACE block (TASK-325, an open edge-chain chute/ramp) has no filled
        /// interior, so it is measured against its rendered RIBBON — the same <see cref="Geometry.SurfaceRibbonQuads"/>
        /// the view draws (a compound of convex segment quads) — so the embed check and the drawn wood can never
        /// disagree on a CONCAVE surface, where a convex-hull measure would lie.
        /// </summary>
        private static void ValidateStaticStartOverlap(LevelData level, List<string> errors)
        {
            if (level.Pieces == null || level.StaticGeometry == null)
                return;

            for (var g = 0; g < level.StaticGeometry.Length; g++)
            {
                var block = level.StaticGeometry[g];
                if (block?.Outline == null)
                    continue;

                ConvexShape staticShape;
                if (block.IsSurface)
                {
                    if (block.Outline.Length < 2)
                        continue; // a chain needs ≥2 points (ValidateStaticGeometry already reports the degenerate case)
                    var quads = Geometry.SurfaceRibbonQuads(block.Outline, block.Surface.Thickness);
                    if (quads.Length == 0)
                        continue;
                    staticShape = ConvexShape.Compound(quads); // concave-honest: max embed over the convex ribbon quads
                }
                else
                {
                    if (block.Outline.Length < 3)
                        continue; // no collider to embed in (degenerate blocks are the view's problem, not this rule's)
                    staticShape = ConvexShape.Polygon(EnsureCcw(block.Outline));
                }

                for (var i = 0; i < level.Pieces.Count; i++)
                {
                    var type = level.Pieces[i].Type;
                    if (type?.Shape == null)
                        continue; // a null type/shape is already reported by ValidatePieces — do not NRE here

                    var pieceShape = ConvexShape.Of(
                        type.Shape, level.Pieces[i].StartPosition, level.Pieces[i].StartRotation);
                    var pen = Geometry.Penetration(pieceShape, staticShape);
                    if (pen > MinStaticPenetration)
                        errors.Add(
                            $"Pieces[{i}] '{type.Kind}' starts embedded in StaticGeometry[{g}] — penetration " +
                            $"{pen:F3} world units at start pose {level.Pieces[i].StartPosition}. A piece may rest " +
                            $"flush on a surface (≤{MinStaticPenetration:0.###}) but must not begin inside it.");
                }
            }
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
                // physics). Surfaces are exempt (edge chain — no split).
                if (!isSurface && outline.Length >= 3 && !IsConvex(outline))
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
                if (!IsConvex(part))
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

        /// <summary>Whether a simple polygon is convex: all cross products of consecutive edges share one sign
        /// (collinear/zero allowed). Tolerant so an authored axis-aligned rectangle (exact) passes cleanly.</summary>
        private static bool IsConvex(Vec2[] v)
        {
            var n = v.Length;
            if (n < 3)
                return false;
            var sign = 0;
            for (var i = 0; i < n; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % n];
                var c = v[(i + 2) % n];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (cross > 1e-6f)
                {
                    if (sign < 0) return false;
                    sign = 1;
                }
                else if (cross < -1e-6f)
                {
                    if (sign > 0) return false;
                    sign = -1;
                }
            }
            return true;
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
                if (shape.ConvexParts != null)
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
            if (shape.ConvexParts != null)
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
