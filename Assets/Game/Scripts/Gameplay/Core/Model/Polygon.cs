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

        public Polygon(Vec2[] outline, Vec2[][] holes = null)
        {
            Outline = outline;
            Holes = holes;
        }
    }
}
