using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Typed level schema — the TASK-004 delta over the GAME_RULES §5 "vocabulary". Newtonsoft-friendly,
    /// stable field names. Numeric thresholds are <c>⟨balancer⟩</c> (starting values, tuned after playtests).
    /// </summary>
    public sealed class LevelData
    {
        public string LevelId { get; set; }
        public Polygon Silhouette { get; set; }
        public IReadOnlyList<BlockDef> BlockPool { get; set; }
        public PoolOrdering PoolOrdering { get; set; } = PoolOrdering.Fixed;

        /// <summary>θ — win coverage threshold in (0,1]. <c>⟨balancer⟩</c>.</summary>
        public float CoverageThreshold { get; set; } = 0.80f;

        public int? AttemptLimit { get; set; }
        public int? MoveLimit { get; set; }

        /// <summary>Fixed spawn point for the floating block (v0 — no aiming, open question #4).</summary>
        public Vec2? DropSpawn { get; set; }
        public Polygon[] StaticGeometry { get; set; }

        public Rect Bounds { get; set; }
        public Vec2 Gravity { get; set; } = new Vec2(0f, -9.81f);
        public int Seed { get; set; }
        public int DifficultyTier { get; set; }
        public LevelSourceKind Source { get; set; } = LevelSourceKind.Authored;
    }
}
