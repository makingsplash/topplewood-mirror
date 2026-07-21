using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Shared unlit material for block/silhouette meshes. Colour is driven per-instance via a
    /// MaterialPropertyBlock, so one material batches all blocks. Prefers URP/Unlit, falls back to the
    /// built-in sprite shader.
    /// </summary>
    public static class BlockMaterial
    {
        public static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");
        public static readonly int LegacyColorProperty = Shader.PropertyToID("_Color");

        private static Material _shared;

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit")
                                 ?? Shader.Find("Sprites/Default")
                                 ?? Shader.Find("Unlit/Color");
                    _shared = new Material(shader) { name = "ShapeFillBlockMaterial" };
                }
                return _shared;
            }
        }
    }
}
