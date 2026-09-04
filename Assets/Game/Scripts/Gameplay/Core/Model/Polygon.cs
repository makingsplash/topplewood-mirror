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

        /// <summary>TASK-346: whether this static builds a physics collider. Default true (every pre-TASK-346 static —
        /// the collider IS the point of an invisible static). A <c>collider:false</c> block is the mirror idiom: a
        /// VISUAL-ONLY static, DRAWN but not collided, so a level can show a solid CONCAVE wood mass (rendered by the
        /// ear-clip triangulator) whose physics stays the authored convex decomposition — a concave FILLED collider is
        /// forbidden (Box2D's coordinate-dependent split, TASK-307). Only meaningful for
        /// <see cref="LevelData.StaticGeometry"/>; the target silhouette leaves the default.</summary>
        public bool HasCollider { get; }

        public Polygon(Vec2[] outline, Vec2[][] holes = null, StaticSurface surface = null, bool hasCollider = true)
        {
            Outline = outline;
            Holes = holes;
            Surface = surface;
            HasCollider = hasCollider;
        }
    }
}
