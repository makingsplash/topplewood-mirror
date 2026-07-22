using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one piece (fills the <c>IPieceView</c> seam). Builds a flat convex mesh from the domain
    /// <see cref="Shape"/> and mirrors the pose each frame. The shared material is double-sided (Cull Off) so
    /// the one-sided fan mesh stays visible as the piece tumbles under physics (TASK-007). Juice is minimal
    /// for the gray-box slice.
    /// </summary>
    public sealed class PieceView : MonoBehaviour, IPieceView
    {
        private static readonly Color DefaultColor = new Color(0.29f, 0.56f, 0.89f); // blue (TargetPreviewView mirrors this)

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Color _baseColor = DefaultColor;

        public void Initialize(Shape shape, string colorHex)
        {
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                _baseColor = parsed;

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = PieceMeshBuilder.Build(shape);

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = BlockMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            _mpb = new MaterialPropertyBlock();
            ApplyColor(_baseColor);
        }

        public void Sync(Vec2 position, float rotation)
        {
            var t = transform;
            t.position = new Vector3(position.X, position.Y, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, rotation * Mathf.Rad2Deg);
        }

        public void SetState(PieceLifecycle state)
        {
            // Subtle feedback: dim while Idle (waiting for a tap), full colour once released/placed.
            var color = state == PieceLifecycle.Idle ? _baseColor * 0.7f : _baseColor;
            ApplyColor(color);
        }

        public void Dispose()
        {
            if (this != null && gameObject != null)
                Destroy(gameObject);
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null)
                return;
            color.a = 1f;
            _mpb.SetColor(BlockMaterial.ColorProperty, color);
            _mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
