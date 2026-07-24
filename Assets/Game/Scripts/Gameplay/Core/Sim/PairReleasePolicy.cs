using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Balance-level prover (TASK-023 §5.3): the intended pair-tap solution as a bot policy. Releases pre-placed
    /// Idle pieces bottom-up BY KIND — the base beam/fulcrum go one at a time (a single instance of their Kind),
    /// then each symmetric load pair (pan → weight → cap) is returned as ONE group so the driver flushes both
    /// ends of the free beam in the same physics step and the net torque stays ≈ 0 (the beam holds level).
    /// Deterministic and RNG-free: picks the Kind whose target slot sits lowest (tie-broken by ordinal Kind
    /// name) and taps EVERY Idle piece of that Kind. The single-order sweep (<see cref="ReleaseOrderPolicy"/>)
    /// stays the NEGATIVE gate — under it a balance level must be stably NOT winnable (one piece at a time
    /// honestly tips the free beam), which is the owner's "по одному не проходится" winnability rule.
    /// </summary>
    public sealed class PairReleasePolicy : ISimBotPolicy
    {
        public bool TryNextRelease(GameModel model, List<Vec2> taps)
        {
            taps.Clear();

            // 1. Choose the next Kind to release: the one whose target slot sits lowest (bottom-up), so the base
            //    goes before the loads that ride it and lower pairs before higher ones. Ordinal name tie-break
            //    keeps it deterministic when two Kinds share a lowest slot Y.
            string bestKind = null;
            var bestY = float.MaxValue;
            foreach (var piece in model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle)
                    continue;

                var slotY = LowestSlotY(model.CurrentLevel.Target, piece.Kind);
                if (slotY < bestY || (slotY == bestY && (bestKind == null || string.CompareOrdinal(piece.Kind, bestKind) < 0)))
                {
                    bestY = slotY;
                    bestKind = piece.Kind;
                }
            }

            if (bestKind == null)
                return false;

            // 2. Tap every Idle piece of that Kind as one synchronous group (base Kind → 1 tap; a load pair → 2).
            foreach (var piece in model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle || piece.Kind != bestKind)
                    continue;
                taps.Add(SimGeometry.ShapeCentroidWorld(model.TypeFor(piece), piece.Position, piece.Rotation));
            }

            return taps.Count > 0;
        }

        private static float LowestSlotY(TargetFigure target, string kind)
        {
            var min = float.MaxValue;
            foreach (var slot in target.Slots)
                if (slot.Kind == kind && slot.Position.Y < min)
                    min = slot.Position.Y;
            return min;
        }
    }
}
