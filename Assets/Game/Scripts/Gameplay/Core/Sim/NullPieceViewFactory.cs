using Gameplay.Core.Adapters;

namespace Gameplay.Core.Sim
{
    /// <summary>Headless <see cref="IPieceViewFactory"/> (RFC §3): hands out no-op views for the sim graph.</summary>
    public sealed class NullPieceViewFactory : IPieceViewFactory
    {
        public IPieceView Create(PieceType type, int placementIndex) => new NullPieceView();
    }
}
