using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Core policy (RFC §4): releases Idle pieces in an explicit order of placement indices, rest-gated. The
    /// host feeds a concrete permutation; the cursor advances one release per call, skipping any index whose
    /// piece is no longer Idle. Always a SINGLE-element group (one piece per release) — the pre-TASK-023
    /// behaviour, unchanged.
    /// </summary>
    public sealed class ReleaseOrderPolicy : ISimBotPolicy
    {
        private readonly int[] _order;
        private int _cursor;

        public ReleaseOrderPolicy(int[] order) => _order = order;

        // Patient: the driver waits for global rest between releases (TASK-060). Unchanged behaviour.
        public bool GatedByRest => true;

        public bool TryNextRelease(GameModel model, List<Vec2> taps)
        {
            taps.Clear();
            while (_cursor < _order.Length)
            {
                var placementIndex = _order[_cursor++];
                var piece = FindIdle(model, placementIndex);
                if (piece == null)
                    continue;

                taps.Add(SimGeometry.TapPoint(model, piece));
                return true;
            }

            return false;
        }

        private static PieceInstance FindIdle(GameModel model, int placementIndex)
        {
            foreach (var piece in model.Pieces)
            {
                if (piece.PlacementIndex == placementIndex && piece.State == PieceLifecycle.Idle)
                    return piece;
            }
            return null;
        }
    }
}
