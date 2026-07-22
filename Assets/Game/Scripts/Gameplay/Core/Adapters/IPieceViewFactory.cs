namespace Gameplay.Core.Adapters
{
    /// <summary>Creates a view for a piece type. Unity impl instantiates the piece GameObject.</summary>
    public interface IPieceViewFactory
    {
        IPieceView Create(PieceType type);
    }
}
