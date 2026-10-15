namespace Gameplay.Core.Adapters
{
    /// <summary>Creates a view for a piece type. Unity impl instantiates the piece GameObject.</summary>
    public interface IPieceViewFactory
    {
        /// <param name="placementIndex">Index of the piece in <c>LevelData.Pieces</c> (deterministic). The view keys
        /// per-instance looks on it — TASK-559: the wood grain phase, so two pieces of one shape do not render an
        /// identical pattern.</param>
        IPieceView Create(PieceType type, int placementIndex);
    }
}
