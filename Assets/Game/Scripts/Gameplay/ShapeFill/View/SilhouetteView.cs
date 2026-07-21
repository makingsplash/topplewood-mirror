using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Static target-silhouette overlay (GAME_RULES §1): a non-colliding outline drawn with a LineRenderer.
    /// Purely visual — it never affects the blocks.
    /// </summary>
    public sealed class SilhouetteView : MonoBehaviour
    {
        private static readonly Color OutlineColor = new Color(0.25f, 0.75f, 1f, 0.9f);

        public void Initialize(Polygon silhouette)
        {
            var outline = silhouette.Outline;
            var line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = outline.Length;
            line.widthMultiplier = 0.08f;
            line.numCornerVertices = 2;
            line.sharedMaterial = BlockMaterial.Shared;
            line.textureMode = LineTextureMode.Stretch;
            line.startColor = OutlineColor;
            line.endColor = OutlineColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (var i = 0; i < outline.Length; i++)
                line.SetPosition(i, new Vector3(outline[i].X, outline[i].Y, 0.1f));
        }
    }
}
