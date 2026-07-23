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

    /// <summary>
    /// Easing curve for the soft-settle glide (TASK-009, game-balancer 13 §2.2). SmootherStep is the default —
    /// its 1st AND 2nd derivatives are zero at both ends, so the glide flows out of rest and into the freeze
    /// with no visible velocity pop. SmoothStep / EaseOutCubic are A/B alternatives (game-balancer 13 §8).
    /// </summary>
    public enum SettleEasing
    {
        SmootherStep,
        SmoothStep,
        EaseOutCubic,
    }
}
