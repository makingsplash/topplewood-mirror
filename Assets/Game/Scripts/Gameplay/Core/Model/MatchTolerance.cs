namespace Gameplay.Core
{
    /// <summary>
    /// matchTolerance = a pair (GAME_RULES §3, RFC §2.2): position tolerance (world units) + angle tolerance
    /// (radians, compared modulo piece symmetry). Replaces the removed scalar CoverageThreshold. Values are
    /// <c>⟨balancer⟩</c> — generous within recognisability, tuned after playtests.
    ///
    /// TASK-043 adds <see cref="RigidityTol"/>: a bound on the spread of per-piece residual vectors after the
    /// greedy match (a rigidity constraint on the assembled figure). Since the assist layer was removed the win is
    /// judged from physical rest poses, so an absolute per-piece tolerance alone cannot tell a genuinely assembled
    /// silhouette from one that collapsed onto the floor with every piece still inside its own generous tolerance
    /// (the grand_seesaw §L9 bug). The rigidity bound catches internal deformation the absolute tolerances miss.
    /// <c>≤ 0</c> ⇒ the constraint is OFF (default, so every level that does not author it is judged byte-for-byte).
    /// </summary>
    public readonly struct MatchTolerance
    {
        public readonly float PosTol;
        public readonly float AngTol;

        /// <summary>Rigidity spread bound (world units): max pairwise difference of the matched pieces' residual
        /// vectors (<c>piece_i − slot_i</c>). Translation-invariant — a uniformly shifted figure has zero spread
        /// (that is what <see cref="PosTol"/> catches), while an internally deformed / collapsed figure has a large
        /// spread. <c>≤ 0</c> disables the constraint (back-compat default).</summary>
        public readonly float RigidityTol;

        public MatchTolerance(float posTol, float angTol, float rigidityTol = 0f)
        {
            PosTol = posTol;
            AngTol = angTol;
            RigidityTol = rigidityTol;
        }
    }
}
