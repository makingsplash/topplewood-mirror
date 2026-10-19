namespace Gameplay.Core
{
    /// <summary>
    /// One (slot, piece) assignment of the greedy pose match (TASK-634): <see cref="SlotIndex"/> indexes
    /// <c>TargetFigure.Slots</c>, <see cref="PlacedIndex"/> indexes the <c>placed</c> list handed to
    /// <see cref="IMatchEvaluator.Evaluate(TargetFigure, System.Collections.Generic.IReadOnlyList{PlacedPiece}, MatchTolerance, System.Collections.Generic.List{MatchPair})"/>.
    /// Describes the LAYOUT the judge built, not the verdict — the win beat flies exactly these pieces into exactly
    /// these slots, so it never builds a matching of its own (identical pieces cannot swap slots mid-air).
    /// </summary>
    public readonly struct MatchPair
    {
        public readonly int SlotIndex;
        public readonly int PlacedIndex;

        public MatchPair(int slotIndex, int placedIndex)
        {
            SlotIndex = slotIndex;
            PlacedIndex = placedIndex;
        }
    }
}
