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
        /// is the tech-debt refinement). Phase 1 emits WARNINGS; flipping <c>EnforceMinDistanceAsError</c> makes them
        /// ERRORS (phase 2, after the corpus is cleaned).
        /// </summary>
        private void ValidateTapSpacing(LevelData level, List<string> errors, List<string> warnings)
        {
            if (level.Pieces == null || level.Pieces.Count < 2)
                return;
            if (!CameraFramingMath.TryContentBounds(level, out var min, out var max))
                return;

            var orthoSize = CameraFramingMath.ComputeOrthoSize(
                min, max, _tapZone.ReferenceAspect,
                _tapZone.RefMargin, _tapZone.RefPreviewBand, _tapZone.RefMinOrthoSize);
            var minWorld = _tapZone.MinTapDp * _tapZone.RefKDevice * orthoSize; // tap-zone DIAMETER in world units

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

                    var msg =
                        $"Tap spacing: Pieces[{i}] '{level.Pieces[i].Type?.Kind}' and Pieces[{j}] " +
                        $"'{level.Pieces[j].Type?.Kind}' start {dist:F3} apart — closer than the {minWorld:F3} world " +
                        $"tap zone ({_tapZone.MinTapDp}dp @ orthoSize {orthoSize:F2}); their tap targets merge.";
                    if (_tapZone.EnforceMinDistanceAsError)
                        errors.Add(msg);
                    else
                        warnings.Add(msg);
                }
            }
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
                else if (type.Shape.Primitive != ShapePrimitive.Circle && type.Shape.Vertices.Length > 8)
                    errors.Add($"Pieces[{i}] '{type.Kind}' has a {type.Shape.Vertices.Length}-vertex polygon collider; " +
                               "Box2D splits any >8-vertex polygon by a coordinate-dependent split (platform-dependent " +
                               "physics, TASK-072/074) — use a native primitive or ≤8 vertices.");
                if (type.Material.Density < 0f || type.Material.Friction < 0f || type.Material.Restitution < 0f)
                    errors.Add($"Pieces[{i}] '{type.Kind}' has a negative material field.");
                if (!level.Bounds.Contains(level.Pieces[i].StartPosition))
                    errors.Add($"Pieces[{i}] '{type.Kind}' start position is outside Bounds.");
            }
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
    }
}
