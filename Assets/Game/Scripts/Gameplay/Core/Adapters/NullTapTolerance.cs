namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// Headless/sim binding of <see cref="IGameplayTapTolerance"/> (TASK-085): radius 0, so the two-tier hit-test's
    /// fallback tier never fires and the domain resolves taps by containment ONLY — byte-for-byte the pre-TASK-085
    /// <c>PickIdlePieceAt</c>. This is what keeps the seeded sweep's poseHash identical: the sim bot taps exact
    /// centroids (inside the polygon → tier-1 always hits), and even structurally the halo tier is disabled here.
    /// Used by <c>SimRun</c> (bare-new domain graph, no VContainer).
    /// </summary>
    public sealed class NullTapTolerance : IGameplayTapTolerance
    {
        public float WorldRadius => 0f;
    }
}
