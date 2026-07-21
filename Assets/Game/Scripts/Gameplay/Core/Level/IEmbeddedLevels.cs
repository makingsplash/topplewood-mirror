using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>Provides the built-in test levels for the vertical slice (no content pipeline in v0).</summary>
    public interface IEmbeddedLevels
    {
        IReadOnlyList<LevelData> Levels { get; }
        LevelData Default { get; }
    }
}
