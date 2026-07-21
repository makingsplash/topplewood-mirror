using Gameplay.Core;
using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one block (fills the <c>IBlockView</c> seam). Builds a flat convex mesh from the
    /// domain <see cref="Shape"/> and mirrors the pose each frame. Juice on settle is intentionally minimal
    /// for the slice (within the perf budget).
    /// </summary>
    public sealed class BlockView : MonoBehaviour, IBlockView
    {
        private static readonly Color DefaultColor = new Color(0.78f, 0.56f, 0.31f); // wood

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Color _baseColor = DefaultColor;

        public void Initialize(Shape shape, string colorHex)
        {
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                _baseColor = parsed;

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildMesh(shape);

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

        public void SetState(BlockLifecycle state)
        {
            // Subtle state feedback: dim while floating, full colour once it belongs to the structure.
            var color = state == BlockLifecycle.Floating ? _baseColor * 0.7f : _baseColor;
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

        private static Mesh BuildMesh(Shape shape)
        {
            var verts = shape.Vertices;
            var vertices = new Vector3[verts.Length];
            for (var i = 0; i < verts.Length; i++)
                vertices[i] = new Vector3(verts[i].X, verts[i].Y, 0f);

            // Convex fan triangulation (v0 blocks are convex, RFC R4).
            var triangles = new int[(verts.Length - 2) * 3];
            var t = 0;
            for (var i = 1; i < verts.Length - 1; i++)
            {
                triangles[t++] = 0;
                triangles[t++] = i;
                triangles[t++] = i + 1;
            }

            var mesh = new Mesh { name = "BlockMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
