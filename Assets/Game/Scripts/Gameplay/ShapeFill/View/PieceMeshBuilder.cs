using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Builds a flat convex fan mesh from a domain <see cref="Shape"/>. Shared by <see cref="PieceView"/>
    /// (the physical pieces) and <see cref="TargetPreviewView"/> (the mini preview) so both render from a
    /// single source of truth — no duplicated triangulation (RFC §4.4). v0 shapes are convex.
    /// Arc primitives are tessellated for the VIEW by <see cref="ShapeVisualOutline"/> (higher segment count
    /// than the ≤8-vertex physics collider, TASK-074) — the physics vertices are never used for rendering.
    /// </summary>
    public static class PieceMeshBuilder
    {
        // View outline, NOT shape.Vertices: dome primitives render at ShapeVisualOutline.VisualArcSegments so the
        // mesh stays smooth while the collider stays at ≤8 vertices (TASK-074). Do not collapse this back to
        // shape.Vertices — that would re-facet the wheels. Straight-edged shapes pass through unchanged.
        public static Mesh Build(Shape shape) => Build(ShapeVisualOutline.Build(shape));

        /// <summary>Build a flat convex fan mesh from a raw CCW outline (TASK-031: visible static geometry reuses
        /// the same triangulation as pieces). The outline must be convex — v0's fan cannot triangulate a concave
        /// polygon (author concave statics as several convex rectangles).</summary>
        public static Mesh Build(Vec2[] verts)
        {
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
