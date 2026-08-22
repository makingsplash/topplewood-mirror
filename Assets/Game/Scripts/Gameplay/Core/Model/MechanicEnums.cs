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

    /// <summary>Provenance of a level (GAME_RULES §5). <see cref="Generated"/> became reachable from data in TASK-185:
    /// the DTO <c>source</c> field parses to it (<c>LevelDataDto.ParseSource</c>); absent → <see cref="Authored"/>, so
    /// every hand-authored level is unchanged. The offline family generator
    /// (<c>Gameplay.Sim.Editor.LevelFamilyGenerator</c>) tags its emitted levels <see cref="Generated"/>. There is
    /// still no RUNTIME consumer of <see cref="LevelData.Source"/> — the tag is descriptive (analytics/QA filter).</summary>
    public enum LevelSourceKind
    {
        Authored,
        Generated,
    }

    /// <summary>
    /// How a level's placed pieces behave physically (TASK-021, free-beam TASK-023, catapult TASK-025 → de-pinned
    /// TASK-027). <see cref="Assembly"/> is the canonical stack: every piece freezes kinematic on rest (snap-to-slot
    /// + rigid support). <see cref="Balance"/> is the seesaw class: the beam is a FREE Dynamic body resting on the
    /// fulcrum (no hinge/pin/limits), loads are released as synchronous groups (a coalescence window merges a pair-tap
    /// into one physics step), the in-fall snap assist is off, and the verdict waits for GLOBAL rest so a swinging beam
    /// is never judged mid-swing. <see cref="Catapult"/> is the decisive-lever sub-class of balance (TASK-025): it
    /// REUSES the balance free-body machinery (no in-fall snap, arm+loads are free Dynamic, verdict at global rest,
    /// BalanceSnapLoads crisps the non-pivot loads). After TASK-027 the pivot arm is NO LONGER pinned (owner playtest
    /// rejected the pin as a "helper"): it rests as a free body on a LOW fulcrum, its charge tilt limited by the
    /// natural ground-stop (a small cube tilts it until the far end touches the ground), and a heavy boulder dropped
    /// on the raised near end flips the arm around the ridge and LAUNCHES the light pebble off the far end; coalescence
    /// is suppressed so the charge→overload chain is released as sequential SINGLE taps. Default is Assembly — old
    /// levels and JSON without a physicsMode field are unchanged.
    /// </summary>
    public enum PhysicsMode
    {
        Assembly,
        Balance,
        Catapult,
    }
}
