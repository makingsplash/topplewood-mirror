namespace Gameplay.Core
{
    /// <summary>
    /// matchTolerance = a pair (GAME_RULES §3, RFC §2.2): position tolerance (world units) + angle tolerance
    /// (radians, compared modulo piece symmetry). Replaces the removed scalar CoverageThreshold. Values are
    /// <c>⟨balancer⟩</c> — generous within recognisability, tuned after playtests.
    /// </summary>
    public readonly struct MatchTolerance
    {
        public readonly float PosTol;
        public readonly float AngTol;

        public MatchTolerance(float posTol, float angTol)
        {
            PosTol = posTol;
            AngTol = angTol;
        }
    }
}
