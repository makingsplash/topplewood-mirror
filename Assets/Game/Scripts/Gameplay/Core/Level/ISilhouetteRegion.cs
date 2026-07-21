namespace Gameplay.Core
{
    /// <summary>
    /// Abstraction over the target region so a raster mask can replace the v0 polygon without touching
    /// coverage/validator (RFC §4).
    /// </summary>
    public interface ISilhouetteRegion
    {
        bool Contains(Vec2 point);
        float Area { get; }
        Rect Bbox { get; }
    }
}
