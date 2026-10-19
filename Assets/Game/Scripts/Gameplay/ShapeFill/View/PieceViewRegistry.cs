using System.Collections.Generic;

namespace Gameplay.View
{
    /// <summary>
    /// The live <see cref="PieceView"/> of each placement of the current level (TASK-634), filled by
    /// <see cref="PieceViewFactory"/>. Keyed by placement index — unique within a level and already the number the domain
    /// hands the factory — so no view reference ever crosses into the domain: the win beat looks a won piece up by the
    /// <c>int</c> in <c>GameModel.WinFigure</c>. A rebuilt level overwrites the keys; a destroyed view reads as absent.
    /// </summary>
    public sealed class PieceViewRegistry
    {
        private readonly Dictionary<int, PieceView> _byPlacement = new();

        public void Register(int placementIndex, PieceView view) => _byPlacement[placementIndex] = view;

        public bool TryGet(int placementIndex, out PieceView view)
        {
            if (_byPlacement.TryGetValue(placementIndex, out view) && view != null)
                return true;
            view = null;
            return false;
        }
    }
}
