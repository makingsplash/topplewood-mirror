using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>Fills the <c>IPieceViewFactory</c> seam: instantiates a <see cref="PieceView"/> per piece type.</summary>
    public sealed class PieceViewFactory : IPieceViewFactory
    {
        private Transform _root;

        private Transform Root
        {
            get
            {
                if (_root == null)
                    _root = new GameObject("ShapeFillPieces").transform;
                return _root;
            }
        }

        public IPieceView Create(PieceType type)
        {
            var go = new GameObject($"PieceView_{type.Kind}");
            go.transform.SetParent(Root, false);
            var view = go.AddComponent<PieceView>();
            view.Initialize(type.Shape, type.Color, type.Kind);
            return view;
        }
    }
}
