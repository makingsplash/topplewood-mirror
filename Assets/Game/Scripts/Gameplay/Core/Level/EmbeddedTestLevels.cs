using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// 1–2 hand-authored test levels embedded in code — deliberately minimal input for the vertical
    /// slice (RFC §2.4), so no content pipeline is fixed yet (open question #3). Numbers are the
    /// balancer's starting values (θ=0.80, "wood" 1.0/0.6/0.05, gravity (0,-9.81), pool ≈1.4× silhouette),
    /// all UNVERIFIED and tuned after playtests.
    /// </summary>
    public sealed class EmbeddedTestLevels : IEmbeddedLevels
    {
        private static readonly MaterialProfile Wood = new MaterialProfile(1.0f, 0.6f, 0.05f);

        // Container holding walls derived from the silhouette footprint (F1, RFC §3). Walls sit OUTSIDE the
        // outline (interior never narrowed) with an open top (drop aperture). ⟨balancer⟩ — UNVERIFIED, tune
        // by lost_count/playtests.
        private const float WallThickness = 0.3f; // w
        private const float WallLip = 0.5f;        // how far walls rise above the silhouette top

        private readonly List<LevelData> _levels;

        public EmbeddedTestLevels()
        {
            _levels = new List<LevelData> { BuildBoxSilhouette(), BuildWideSilhouette() };
        }

        public IReadOnlyList<LevelData> Levels => _levels;
        public LevelData Default => _levels[0];

        // Level 1: fill a 2×2 square. Silhouette area = 4; pool = 6 unit boxes (area 6 ≈ 1.5× → margin).
        private static LevelData BuildBoxSilhouette()
        {
            var silhouette = new Polygon(new[]
            {
                new Vec2(-1f, -1f),
                new Vec2(1f, -1f),
                new Vec2(1f, 1f),
                new Vec2(-1f, 1f),
            });

            return new LevelData
            {
                LevelId = "slice_box_2x2",
                Silhouette = silhouette,
                BlockPool = new List<BlockDef>
                {
                    new BlockDef("wood_unit", Shape.Box(1f, 1f), Wood, count: 6),
                },
                PoolOrdering = PoolOrdering.Fixed,
                CoverageThreshold = 0.80f,
                AttemptLimit = null,
                MoveLimit = null,
                DropSpawn = new Vec2(0f, 5f),
                StaticGeometry = BuildContainer(-1f, 1f, -1f, 1f),
                Bounds = new Rect(new Vec2(-6f, -6f), new Vec2(6f, 10f)),
                Gravity = new Vec2(0f, -9.81f),
                Seed = 1,
                DifficultyTier = 1,
                Source = LevelSourceKind.Authored,
            };
        }

        // Level 2: fill a wide 3×1 strip. Silhouette area = 3; pool = 5 unit boxes (area 5 ≈ 1.6× → margin).
        private static LevelData BuildWideSilhouette()
        {
            var silhouette = new Polygon(new[]
            {
                new Vec2(-1.5f, -0.5f),
                new Vec2(1.5f, -0.5f),
                new Vec2(1.5f, 0.5f),
                new Vec2(-1.5f, 0.5f),
            });

            return new LevelData
            {
                LevelId = "slice_wide_3x1",
                Silhouette = silhouette,
                BlockPool = new List<BlockDef>
                {
                    new BlockDef("wood_unit", Shape.Box(1f, 1f), Wood, count: 5),
                },
                PoolOrdering = PoolOrdering.Fixed,
                CoverageThreshold = 0.80f,
                DropSpawn = new Vec2(0f, 5f),
                StaticGeometry = BuildContainer(-1.5f, 1.5f, -0.5f, 0.5f),
                Bounds = new Rect(new Vec2(-6f, -6f), new Vec2(6f, 10f)),
                Gravity = new Vec2(0f, -9.81f),
                Seed = 2,
                DifficultyTier = 1,
                Source = LevelSourceKind.Authored,
            };
        }

        /// <summary>
        /// Deterministic footprint→container rule (F1, RFC §3.1): three rectangular walls (floor + left +
        /// right) placed OUTSIDE the silhouette bbox, open at the top (drop aperture). Inner faces align to
        /// the bbox edges so the fillable interior is never narrowed → coverage≥θ stays reachable.
        /// </summary>
        private static Polygon[] BuildContainer(float minX, float maxX, float minY, float maxY)
        {
            const float w = WallThickness;
            const float lip = WallLip;
            var top = maxY + lip;

            var floor = new Polygon(new[]
            {
                new Vec2(minX - w, minY - w),
                new Vec2(maxX + w, minY - w),
                new Vec2(maxX + w, minY),
                new Vec2(minX - w, minY),
            });
            var leftWall = new Polygon(new[]
            {
                new Vec2(minX - w, minY),
                new Vec2(minX, minY),
                new Vec2(minX, top),
                new Vec2(minX - w, top),
            });
            var rightWall = new Polygon(new[]
            {
                new Vec2(maxX, minY),
                new Vec2(maxX + w, minY),
                new Vec2(maxX + w, top),
                new Vec2(maxX, top),
            });

            return new[] { floor, leftWall, rightWall };
        }
    }
}
