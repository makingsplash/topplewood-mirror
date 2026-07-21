namespace Gameplay.Core
{
    /// <summary>
    /// Generic physics-world seam (overview): Step + Hash, no physics types. Mechanic-specific body
    /// operations live in <c>IShapeFillSimulation</c> which extends this in domain types.
    /// </summary>
    public interface IGameplaySimulation
    {
        /// <summary>Advance the world by one fixed step.</summary>
        void Step(float dt);

        /// <summary>Quantised-pose hash for reproducibility checks (ADR-0001 M0.1; reproducibility deferred).</summary>
        long Hash();
    }
}
