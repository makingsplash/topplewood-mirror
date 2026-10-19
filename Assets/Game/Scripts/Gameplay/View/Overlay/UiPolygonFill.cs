using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>Solid fill of N pre-triangulated polygons in canvas-local units (TASK-613, T1): the hole pass of
    /// <see cref="SpotlightDim"/>, the tour's miss flash and the win beat's window flash (TASK-634; the TASK-613 "Совпало"
    /// ghost that it also drew is gone). Triangulation is the caller's
    /// (<c>Geometry.TriangulateConcave</c>, once per shape change) — this graphic only re-emits vertices, so moving the
    /// polygons every frame does not re-triangulate. Vertex-colour tinted default UI shader, no texture.
    ///
    /// <c>[RequireComponent(typeof(CanvasRenderer))]</c> is mandatory on the concrete type (TASK-309, see
    /// <see cref="UiContour"/>).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiPolygonFill : MaskableGraphic
    {
        private readonly List<Vector2[]> _points = new();
        private readonly List<int[]> _triangles = new();

        /// <summary>Replace every polygon. <paramref name="triangles"/>[i] indexes into <paramref name="points"/>[i].</summary>
        public void SetPolygons(IReadOnlyList<Vector2[]> points, IReadOnlyList<int[]> triangles)
        {
            _points.Clear();
            _triangles.Clear();
            if (points != null && triangles != null)
                for (var i = 0; i < points.Count && i < triangles.Count; i++)
                {
                    if (points[i] == null || triangles[i] == null)
                        continue;
                    _points.Add(points[i]);
                    _triangles.Add(triangles[i]);
                }
            SetVerticesDirty();
        }

        public void ClearPolygons() => SetPolygons(null, null);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var c = (Color32)color;
            var baseIndex = 0;
            for (var p = 0; p < _points.Count; p++)
            {
                var pts = _points[p];
                foreach (var pt in pts)
                {
                    var v = UIVertex.simpleVert;
                    v.color = c;
                    v.position = new Vector3(pt.x, pt.y, 0f);
                    vh.AddVert(v);
                }
                var tris = _triangles[p];
                for (var t = 0; t + 2 < tris.Length; t += 3)
                    vh.AddTriangle(baseIndex + tris[t], baseIndex + tris[t + 1], baseIndex + tris[t + 2]);
                baseIndex += pts.Length;
            }
        }
    }
}
