using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Heuristic "bottom-up" prover (RFC §4): among Idle pieces, releases the one whose target slot sits
    /// lowest (min Y over slots matching its Kind), tie-broken by placement index. Deterministic and RNG-free —
    /// this is the designer's intended solution order and a fast smoke prover of solvability. Always a
    /// SINGLE-element group (one piece per release).
    /// </summary>
    public sealed class SlotAscendingPolicy : ISimBotPolicy
    {
        public bool TryNextRelease(GameModel model, List<Vec2> taps)
        {
            taps.Clear();

            PieceInstance best = null;
            var bestY = float.MaxValue;
            var bestIndex = int.MaxValue;

            foreach (var piece in model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle)
                    continue;

                var targetY = LowestSlotY(model.CurrentLevel.Target, piece.Kind);
                if (targetY < bestY || (targetY == bestY && piece.PlacementIndex < bestIndex))
                {
                    best = piece;
                    bestY = targetY;
                    bestIndex = piece.PlacementIndex;
                }
            }

            if (best == null)
                return false;

            taps.Add(SimGeometry.ShapeCentroidWorld(model.TypeFor(best), best.Position, best.Rotation));
            return true;
        }

        private static float LowestSlotY(TargetFigure target, string kind)
        {
            var min = float.MaxValue;
            foreach (var slot in target.Slots)
            {
                if (slot.Kind == kind && slot.Position.Y < min)
                    min = slot.Position.Y;
            }
            return min;
        }
    }
}
