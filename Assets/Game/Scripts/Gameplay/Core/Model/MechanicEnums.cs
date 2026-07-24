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
    /// How a level's placed pieces behave physically (TASK-021, free-beam TASK-023). <see cref="Assembly"/> is the
    /// canonical stack: every piece freezes kinematic on rest (snap-to-slot + rigid support). <see cref="Balance"/>
    /// is the seesaw class: the beam is a FREE Dynamic body resting on the fulcrum (no hinge/pin/limits), loads are
    /// released as synchronous groups (a coalescence window merges a pair-tap into one physics step), the in-fall
    /// snap assist is off, and the verdict waits for GLOBAL rest so a swinging beam is never judged mid-swing.
    /// Default is Assembly — old levels and JSON without a physicsMode field are unchanged.
    /// </summary>
    public enum PhysicsMode
    {
        Assembly,
        Balance,
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
