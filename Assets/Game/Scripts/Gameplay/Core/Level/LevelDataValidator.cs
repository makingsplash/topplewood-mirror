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
        public ValidationResult Validate(LevelData level)
        {
            var errors = new List<string>();

            if (level == null)
            {
                errors.Add("LevelData is null.");
                return new ValidationResult(errors);
            }

            // 1. Target figure: outline ≥3 verts, non-empty slots.
            ValidateTarget(level, errors);

            // 2. Pieces: non-empty, each shape ≥3 verts, non-negative material, start pose in Bounds.
            ValidatePieces(level, errors);

            // 3. Kind coverage: for every slot Kind there must be at least as many pieces of that Kind.
            ValidateKindCoverage(level, errors);

            // 4. Tolerance: PosTol > 0, AngTol ≥ 0.
            if (level.MatchTolerance.PosTol <= 0f)
                errors.Add($"MatchTolerance.PosTol must be > 0; was {level.MatchTolerance.PosTol}.");
            if (level.MatchTolerance.AngTol < 0f)
                errors.Add($"MatchTolerance.AngTol must be ≥ 0; was {level.MatchTolerance.AngTol}.");

            // 5. Bounds / gravity.
            if (level.Bounds.IsDegenerate)
                errors.Add("Bounds are degenerate.");
            if (level.Gravity.SqrLength <= 0f)
                errors.Add("Gravity must be non-zero.");

            // 6. Balance mode (TASK-021): structural shape of a seesaw level (winnability is level-sim's job).
            if (level.PhysicsMode == PhysicsMode.Balance)
                ValidateBalance(level, errors);

            return new ValidationResult(errors);
        }

        /// <summary>
        /// Balance-mode structural checks (TASK-021): the level must have at least one pivot piece, and every
        /// pivot Kind must map to exactly ONE in-Bounds slot — the hinge anchor is a single point, so two slots
        /// of a pivot Kind (or none) is a broken authoring, not a solvable level. Winnability under a live crank
        /// is proven by level-sim, not here.
        /// </summary>
        private static void ValidateBalance(LevelData level, List<string> errors)
        {
            if (level.Pieces == null)
                return;

            var pivotKinds = new HashSet<string>();
            foreach (var placement in level.Pieces)
                if (placement.Type != null && placement.Type.Pivot)
                    pivotKinds.Add(placement.Type.Kind);

            if (pivotKinds.Count == 0)
            {
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
                    errors.Add($"Pivot kind '{kind}' has no target slot to anchor its hinge.");
                else if (count > 1)
                    errors.Add($"Pivot kind '{kind}' has {count} slots; a hinge needs exactly one anchor slot.");
                else if (!level.Bounds.Contains(anchor))
                    errors.Add($"Pivot kind '{kind}' hinge anchor {anchor} is outside Bounds.");
            }
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
                var kind = placement.Type?.Kind;
                if (kind == null)
                    continue;
                pieceCounts[kind] = pieceCounts.TryGetValue(kind, out var c) ? c + 1 : 1;
            }

            foreach (var kv in slotCounts)
            {
                pieceCounts.TryGetValue(kv.Key, out var have);
                if (have < kv.Value)
                    errors.Add($"Kind '{kv.Key}': {kv.Value} slot(s) but only {have} piece(s) — unwinnable.");
            }
        }
    }
}
