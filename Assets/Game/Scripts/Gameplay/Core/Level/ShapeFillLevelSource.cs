namespace Gameplay.Core
{
    /// <summary>
    /// Fills the <c>ILevelSource</c> seam for shape_fill. v0 serves an embedded test level (the default);
    /// authored/generated content is a later task.
    /// </summary>
    public sealed class ShapeFillLevelSource : ILevelSource
    {
        private readonly IEmbeddedLevels _levels;

        public ShapeFillLevelSource(IEmbeddedLevels levels)
        {
            _levels = levels;
        }

        public LevelData Load() => _levels.Default;
    }
}
