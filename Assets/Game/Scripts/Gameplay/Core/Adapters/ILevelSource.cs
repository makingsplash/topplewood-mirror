namespace Gameplay.Core
{
    /// <summary>
    /// Level provider seam (overview §Швы механики). TASK-004 introduces the typed <see cref="LevelData"/>;
    /// v0 impl serves 1–2 embedded test levels.
    /// </summary>
    public interface ILevelSource
    {
        LevelData Load();
    }
}
