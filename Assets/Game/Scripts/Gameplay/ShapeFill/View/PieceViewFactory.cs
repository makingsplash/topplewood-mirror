using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>Fills the <c>IPieceViewFactory</c> seam: instantiates a <see cref="PieceView"/> per piece type and
    /// registers it by placement index in <see cref="PieceViewRegistry"/> (TASK-634: the win beat flies these views).</summary>
    public sealed class PieceViewFactory : IPieceViewFactory
    {
        private readonly PieceViewRegistry _registry;
        private Transform _root;

        public PieceViewFactory(PieceViewRegistry registry) => _registry = registry;

        private Transform Root
        {
            get
            {
                if (_root == null)
                    _root = new GameObject("ShapeFillPieces").transform;
                return _root;
            }
        }

        public IPieceView Create(PieceType type, int placementIndex)
        {
            var go = new GameObject($"PieceView_{type.Kind}");
            go.transform.SetParent(Root, false);
            var view = go.AddComponent<PieceView>();
            view.Initialize(type.Shape, type.Color, type.Kind, placementIndex);
            _registry.Register(placementIndex, view);
            return view;
        }
    }
}
