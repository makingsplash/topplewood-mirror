using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Structural, deterministic level-schema validator (RFC §5). Catches broken schema only — statistical
    /// solvability (pass-rate over seeded strategies) is level-sim's job (GAME_RULES §4), not this.
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

            // 1. Silhouette structural sanity.
            var region = ValidateSilhouette(level, errors);

            // 2. Coverage threshold in (0,1].
            if (level.CoverageThreshold <= 0f || level.CoverageThreshold > 1f)
                errors.Add($"CoverageThreshold must be in (0,1]; was {level.CoverageThreshold}.");

            // 3. Pool + shapes + material sanity.
            var totalBlockArea = ValidatePool(level, errors);

            // 4. Lower-bound solvability sanity: Σ block area ≥ silhouette area · θ.
            if (region != null && level.CoverageThreshold > 0f)
            {
                var needed = region.Area * level.CoverageThreshold;
                if (totalBlockArea < needed)
                    errors.Add($"Pool area {totalBlockArea:0.###} < silhouette·θ {needed:0.###} — level is unwinnable.");
            }

            // 5. Bounds / drop spawn / gravity.
            if (level.Bounds.IsDegenerate)
                errors.Add("Bounds are degenerate.");
            if (level.DropSpawn.HasValue && !level.Bounds.Contains(level.DropSpawn.Value))
                errors.Add("DropSpawn is outside Bounds.");
            if (level.Gravity.SqrLength <= 0f)
                errors.Add("Gravity must be non-zero.");

            return new ValidationResult(errors);
        }

        private static PolygonSilhouetteRegion ValidateSilhouette(LevelData level, List<string> errors)
        {
            if (level.Silhouette?.Outline == null || level.Silhouette.Outline.Length < 3)
            {
                errors.Add("Silhouette outline must have at least 3 vertices.");
                return null;
            }

            var region = new PolygonSilhouetteRegion(level.Silhouette);
            if (region.Area <= 0f)
                errors.Add("Silhouette area must be positive (degenerate outline).");
            return region;
        }

        private static float ValidatePool(LevelData level, List<string> errors)
        {
            if (level.BlockPool == null || level.BlockPool.Count == 0)
            {
                errors.Add("BlockPool is empty.");
                return 0f;
            }

            var total = 0f;
            for (var i = 0; i < level.BlockPool.Count; i++)
            {
                var def = level.BlockPool[i];
                if (def.Shape?.Vertices == null || def.Shape.Vertices.Length < 3)
                {
                    errors.Add($"BlockPool[{i}] '{def?.Kind}' has an invalid shape (need ≥3 vertices).");
                    continue;
                }
                if (def.Material.Density < 0f || def.Material.Friction < 0f || def.Material.Restitution < 0f)
                    errors.Add($"BlockPool[{i}] '{def.Kind}' has a negative material field.");

                total += ShapeArea(def.Shape) * def.Count;
            }
            return total;
        }

        private static float ShapeArea(Shape shape)
        {
            var verts = shape.Vertices;
            var sum = 0f;
            for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++)
                sum += (verts[j].X * verts[i].Y) - (verts[i].X * verts[j].Y);
            return System.MathF.Abs(sum) * 0.5f;
        }
    }
}
