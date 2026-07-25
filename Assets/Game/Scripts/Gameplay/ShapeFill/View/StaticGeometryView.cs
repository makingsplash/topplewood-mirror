using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one VISIBLE static geometry block (TASK-031c). Static geometry is invisible by default
    /// (it only builds a collider); a level opts a block in via <c>visible:true</c> and this renders its outline
    /// as a flat convex-fan mesh (same builder + shared double-sided material as <see cref="PieceView"/>). The
    /// outline is authored in WORLD space, so the mesh is built from it directly and the transform sits at the
    /// origin, nudged slightly behind the pieces on Z so falling pieces read in front.
    /// </summary>
    public sealed class StaticGeometryView : MonoBehaviour
    {
        private const float ZBehindPieces = 0.5f; // pieces live at z=0; larger z = further from the +Z-facing camera

        private static readonly Color DefaultColor = new Color(0.72f, 0.57f, 0.42f); // neutral block brown

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;

        public void Initialize(Vec2[] worldOutline, string colorHex)
        {
            var color = DefaultColor;
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                color = parsed;

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = PieceMeshBuilder.Build(worldOutline);

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            transform.position = new Vector3(0f, 0f, ZBehindPieces);

            _mpb = new MaterialPropertyBlock();
            color.a = 1f;
            _mpb.SetColor(BlockMaterial.ColorProperty, color);
            _mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
