namespace Gameplay.Core
{
    /// <summary>
    /// Authoring type of a target piece (GAME_RULES §1). Slots of the same <see cref="Kind"/> are
    /// interchangeable (identity-agnostic match). <see cref="Color"/> is v0 cosmetic (win rule is
    /// colour-agnostic; "colour as match" is an A/B). <see cref="SymmetryOrder"/> is the rotational
    /// symmetry period used by pose-match angle comparison (box=4, triangle/half-disc=1).
    /// Replaces the removed pool-model BlockDef (no Count — pieces are pre-placed 1:1, not pooled).
    /// </summary>
    public sealed class PieceType
    {
        public string Kind { get; }
        public Shape Shape { get; }
        public MaterialProfile Material { get; }
        public string Color { get; }
        public int SymmetryOrder { get; }

        /// <summary>Balance-mode hinge beam (TASK-021): on rest it is world-pinned (Dynamic, damped) instead of
        /// frozen kinematic, so weight on one side tilts it. Only meaningful when the level is PhysicsMode.Balance.</summary>
        public bool Pivot { get; }

        public PieceType(string kind, Shape shape, MaterialProfile material, string color = null, int symmetryOrder = 1,
            bool pivot = false)
        {
            Kind = kind;
            Shape = shape;
            Material = material;
            Color = color;
            SymmetryOrder = symmetryOrder < 1 ? 1 : symmetryOrder;
            Pivot = pivot;
        }
    }
}
