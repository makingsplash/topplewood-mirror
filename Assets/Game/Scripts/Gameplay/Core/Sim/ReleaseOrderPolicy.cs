namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Core policy (RFC §4): releases Idle pieces in an explicit order of placement indices, rest-gated. The
    /// host feeds a concrete permutation; the cursor advances one release per call, skipping any index whose
    /// piece is no longer Idle.
    /// </summary>
    public sealed class ReleaseOrderPolicy : ISimBotPolicy
    {
        private readonly int[] _order;
        private int _cursor;

        public ReleaseOrderPolicy(int[] order) => _order = order;

        public bool TryNextRelease(GameModel model, out Vec2 tapPoint)
        {
            while (_cursor < _order.Length)
            {
                var placementIndex = _order[_cursor++];
                var piece = FindIdle(model, placementIndex);
                if (piece == null)
                    continue;

                tapPoint = SimGeometry.ShapeCentroidWorld(model.TypeFor(piece), piece.Position, piece.Rotation);
                return true;
            }

            tapPoint = Vec2.Zero;
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
