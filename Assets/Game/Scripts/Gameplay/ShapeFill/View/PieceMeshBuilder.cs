using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Builds a flat convex fan mesh from a domain <see cref="Shape"/>. Shared by <see cref="PieceView"/>
    /// (the physical pieces) and <see cref="TargetPreviewView"/> (the mini preview) so both render from a
    /// single source of truth — no duplicated triangulation (RFC §4.4). v0 shapes are convex.
    /// </summary>
    public static class PieceMeshBuilder
    {
        public static Mesh Build(Shape shape)
        {
            var verts = shape.Vertices;
            var vertices = new Vector3[verts.Length];
            for (var i = 0; i < verts.Length; i++)
                vertices[i] = new Vector3(verts[i].X, verts[i].Y, 0f);

            // Convex fan triangulation (v0 pieces are convex).
            var triangles = new int[(verts.Length - 2) * 3];
            var t = 0;
            for (var i = 1; i < verts.Length - 1; i++)
            {
                triangles[t++] = 0;
                triangles[t++] = i;
                triangles[t++] = i + 1;
            }

            var mesh = new Mesh { name = "PieceMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
