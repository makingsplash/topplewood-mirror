namespace Gameplay.Core
{
    /// <summary>Canonical piece life-cycle (GAME_RULES §2): all pieces start Idle, tap releases one.</summary>
    public enum PieceLifecycle
    {
        Idle,
        Falling,
        Placed,
        Lost,
    }

    /// <summary>Provenance of a level (GAME_RULES §5). v0 = Authored.</summary>
    public enum LevelSourceKind
    {
        Authored,
        Generated,
    }
}
