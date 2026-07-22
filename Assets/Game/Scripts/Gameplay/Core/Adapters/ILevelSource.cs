namespace Gameplay.Core
{
    /// <summary>
    /// Level provider seam (overview §Швы механики). TASK-004 introduces the typed <see cref="LevelData"/>;
    /// TASK-009 turns it into an ordered campaign: <see cref="Count"/> levels indexed 0..Count-1, served by
    /// index so the campaign controller can advance/retry. Implementations clamp an out-of-range index.
    /// </summary>
    public interface ILevelSource
    {
        int Count { get; }

        LevelData Load(int index);
    }
}
