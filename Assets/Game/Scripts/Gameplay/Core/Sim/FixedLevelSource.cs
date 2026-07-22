namespace Gameplay.Core.Sim
{
    /// <summary>
    /// <see cref="ILevelSource"/> that serves one explicit level (RFC §7 п.1): the sweep drives a specific
    /// <see cref="LevelData"/> per run instead of the embedded <c>Default</c>. Unity-free.
    /// </summary>
    public sealed class FixedLevelSource : ILevelSource
    {
        private readonly LevelData _level;

        public FixedLevelSource(LevelData level) => _level = level;

        public int Count => 1;

        public LevelData Load(int index) => _level;
    }
}
