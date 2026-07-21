namespace Gameplay.Core
{
    /// <summary>Versioned save envelope marker (overview §Сейв). TASK-004 bumps schema 1 → 2.</summary>
    public interface IGameSnapshot
    {
        int SchemaVersion { get; }
    }
}
