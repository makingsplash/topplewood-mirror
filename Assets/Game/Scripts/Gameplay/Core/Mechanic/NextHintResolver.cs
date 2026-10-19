using System.Collections.Generic;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Pure, headless domain query behind the Hint booster (architect §2): "which Idle piece is next in the
    /// certified winning order, given the current field?". It mirrors the tour's target resolution (<c>TapPieceStep</c>) —
    /// walk the order, return the first placement index whose piece is currently <see cref="PieceLifecycle.Idle"/>.
    /// It reads the model read-only (like an <c>ITutorialStep</c>, never mutates), so the headless-simulatable
    /// invariant holds; the sim never calls it. A null/empty order → -1 (honest no-op — the hint never GUESSES an
    /// order, so a level with no baked <c>WinningOrder</c> simply has no hint).
    /// </summary>
    public static class NextHintResolver
    {
        /// <summary>InstanceId of the next Idle piece in <paramref name="winningOrder"/>, or -1 when there is
        /// nothing to reveal (no order, or no listed piece is still Idle).</summary>
        public static int NextIdleInstanceId(GameModel model, IReadOnlyList<int> winningOrder)
        {
            if (model == null || winningOrder == null)
                return -1;

            foreach (var placementIndex in winningOrder)
            {
                foreach (var piece in model.Pieces)
                {
                    if (piece.PlacementIndex == placementIndex && piece.State == PieceLifecycle.Idle)
                        return piece.InstanceId;
                }
            }

            return -1;
        }

        /// <summary>k = number of tapable (non-preplaced) pieces — the functionality threshold source (balancer
        /// §2.2). Static over <see cref="LevelData"/>, no sim needed.</summary>
        public static int TapablePieceCount(LevelData level)
        {
            if (level?.Pieces == null)
                return 0;

            var count = 0;
            foreach (var piece in level.Pieces)
                if (!piece.Preplaced)
                    count++;
            return count;
        }
    }
}
