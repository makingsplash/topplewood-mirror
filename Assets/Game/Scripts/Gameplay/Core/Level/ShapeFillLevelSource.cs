namespace Gameplay.Core
{
    /// <summary>
    /// Embedded-backed <see cref="ILevelSource"/>: serves the in-code <see cref="IEmbeddedLevels"/> as an
    /// ordered campaign. TASK-009 ships authored JSON content via <see cref="JsonLevelSource"/> (the bound
    /// impl); this remains a valid engine-free fallback over the hardcoded levels (and the source the
    /// level-sim harness exercises).
    /// </summary>
    public sealed class ShapeFillLevelSource : ILevelSource
    {
        private readonly IEmbeddedLevels _levels;

        public ShapeFillLevelSource(IEmbeddedLevels levels)
        {
            _levels = levels;
        }

        public int Count => _levels.Levels.Count;

        public LevelData Load(int index)
        {
            var levels = _levels.Levels;
            if (index < 0)
                index = 0;
            else if (index >= levels.Count)
                index = levels.Count - 1;
            return levels[index];
        }
    }
}
