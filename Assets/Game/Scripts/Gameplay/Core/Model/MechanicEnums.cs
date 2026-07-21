namespace Gameplay.Core
{
    /// <summary>Canonical block life-cycle (GAME_RULES §2).</summary>
    public enum BlockLifecycle
    {
        Floating,
        Falling,
        Settled,
        Lost,
    }

    /// <summary>How the pool feeds blocks to the player (GAME_RULES §1). v0 default = Fixed.</summary>
    public enum PoolOrdering
    {
        Fixed,
        Bag,
        PlayerChoice,
    }

    /// <summary>Provenance of a level (GAME_RULES §5). v0 = Authored.</summary>
    public enum LevelSourceKind
    {
        Authored,
        Generated,
    }
}
