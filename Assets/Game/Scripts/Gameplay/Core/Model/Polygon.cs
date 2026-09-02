namespace Gameplay.Core
{
    /// <summary>
    /// Domain polygon: an outline plus optional holes. Used for the target silhouette (v0) and
    /// optional static geometry. Rasterisation / containment lives behind <c>ISilhouetteRegion</c>
    /// so a raster mask can replace the polygon later without touching evaluators/validator.
    /// </summary>
    public sealed class Polygon
    {
        public Vec2[] Outline { get; }
        public Vec2[][] Holes { get; }

        /// <summary>TASK-325 (S2): when non-null, this static-geometry block is a SURFACE — the <see cref="Outline"/>
        /// is an OPEN polyline traced by an <c>EdgeCollider2D</c> chain (a chute/ramp), not a filled solid. Null ⇒
        /// the default filled block (every pre-TASK-325 static). Only meaningful for <see cref="LevelData.StaticGeometry"/>;
        /// the target silhouette never sets it.</summary>
        public StaticSurface Surface { get; }

        /// <summary>TASK-325: true when this block is an open edge-chain surface rather than a filled solid.</summary>
        public bool IsSurface => Surface != null;

        public Polygon(Vec2[] outline, Vec2[][] holes = null, StaticSurface surface = null)
        {
            Outline = outline;
            Holes = holes;
            Surface = surface;
        }
    }
}
