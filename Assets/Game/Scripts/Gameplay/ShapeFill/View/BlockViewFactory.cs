using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>Fills the <c>IBlockViewFactory</c> seam: instantiates a <see cref="BlockView"/> per block.</summary>
    public sealed class BlockViewFactory : IBlockViewFactory
    {
        private Transform _root;

        private Transform Root
        {
            get
            {
                if (_root == null)
                    _root = new GameObject("ShapeFillBlocks").transform;
                return _root;
            }
        }

        public IBlockView Create(BlockDef def)
        {
            var go = new GameObject($"BlockView_{def.Kind}");
            go.transform.SetParent(Root, false);
            var view = go.AddComponent<BlockView>();
            view.Initialize(def.Shape, def.Color);
            return view;
        }
    }
}
