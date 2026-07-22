using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Static target overlay (GAME_RULES §Q5): a non-colliding outline drawn with a LineRenderer. Purely
    /// visual — it never affects the pieces. Used for both the union outline and per-slot ghost outlines
    /// (a paler colour), so the player sees WHERE each piece belongs.
    /// </summary>
    public sealed class SilhouetteView : MonoBehaviour
    {
        private static readonly Color OutlineColor = new Color(0.25f, 0.75f, 1f, 0.9f);

        public void Initialize(Polygon polygon) => Initialize(polygon, OutlineColor);

        public void Initialize(Polygon polygon, Color color)
        {
            var outline = polygon.Outline;
            var line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = outline.Length;
            line.widthMultiplier = 0.08f;
            line.numCornerVertices = 2;
            line.sharedMaterial = BlockMaterial.Shared;
            line.textureMode = LineTextureMode.Stretch;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (var i = 0; i < outline.Length; i++)
                line.SetPosition(i, new Vector3(outline[i].X, outline[i].Y, 0.1f));
        }
    }
}
