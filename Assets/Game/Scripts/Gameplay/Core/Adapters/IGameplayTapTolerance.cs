namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// Headless seam (TASK-085) for the SCREEN-space tap tolerance the domain hit-test needs but must not compute
    /// itself — the world radius of the fallback halo around a piece when a tap misses every polygon. Same family
    /// as <see cref="IGameplayClock"/> / <c>IGameplaySimulation</c>: the domain stays Unity-free and
    /// headless-simulatable, and the Unity contact (Camera/Screen) lives behind the adapter. The runtime binding
    /// (<c>CameraTapTolerance</c>) reads the live camera orthoSize + screen dpi/height; the sim/headless binding
    /// (<see cref="NullTapTolerance"/>) returns 0 so the fallback tier is inert in the seeded sweep.
    /// </summary>
    public interface IGameplayTapTolerance
    {
        /// <summary>Current world-space radius of the tap halo, or 0 when the tolerance is disabled (headless/sim).
        /// Recomputed per read from the live camera so a per-level reframe is reflected without any invalidation.</summary>
        float WorldRadius { get; }
    }
}
