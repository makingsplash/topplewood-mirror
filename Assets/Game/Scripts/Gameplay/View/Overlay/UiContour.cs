using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>A closed constant-width stroke tracing a set of canvas-local points (TASK-083; extracted from the former
    /// <c>TutorialView.OutlineContour</c> in TASK-613 so the tour and the hint highlight share it). UI-mesh only
    /// (vertex-color tinted default shader, no material) — the piece material <c>BlockMaterial</c> is not involved.
    /// Corners use per-edge quads that may overlap slightly; acceptable for a highlight (TASK-083 §3.2).
    ///
    /// <c>[RequireComponent(typeof(CanvasRenderer))]</c> is MANDATORY here even though the base <c>Graphic</c>
    /// already declares it (TASK-309 root cause): <c>RequireComponent</c> is resolved on the CONCRETE type given
    /// to <c>AddComponent&lt;T&gt;()</c> and is NOT inherited, so <c>Graphic</c>'s copy never fires for a subclass.
    /// Without it, <c>AddComponent&lt;UiContour&gt;()</c> left the GameObject with no <c>CanvasRenderer</c>,
    /// so the mesh had nothing to draw through — every projection number was correct yet zero pixels reached the
    /// frame. This is why every built-in UI graphic (<c>Image</c>, <c>Text</c>, <c>RawImage</c>) RE-declares the
    /// same attribute on its own class rather than relying on <c>Graphic</c>'s.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiContour : MaskableGraphic
    {
        private Vector2[] _points;

        private float _width = 6f;
        private float _reveal = 1f;

        public float Width
        {
            get => _width;
            set
            {
                if (Mathf.Approximately(_width, value))
                    return;
                _width = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Fraction of the perimeter drawn, from the first point along the ring (TASK-613: the "Совпало"
        /// contour draws itself on). 1 = closed ring.</summary>
        public float Reveal
        {
            get => _reveal;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_reveal, value))
                    return;
                _reveal = value;
                SetVerticesDirty();
            }
        }

        // No sprite/texture: the base Graphic.mainTexture returns the built-in white texture, tinted by the
        // vertex colour via the default UI shader — no material of our own, BlockMaterial untouched.

        public void SetPoints(Vector2[] points)
        {
            _points = points;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_points == null || _points.Length < 2 || _reveal <= 0f)
                return;

            var half = Width * 0.5f;
            var n = _points.Length;
            var budget = float.MaxValue;
            if (_reveal < 1f)
            {
                var perimeter = 0f;
                for (var i = 0; i < n; i++)
                    perimeter += (_points[(i + 1) % n] - _points[i]).magnitude;
                budget = perimeter * _reveal;
            }
            var vi = 0;
            for (var i = 0; i < n && budget > 0f; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % n];
                var dir = b - a;
                var len = dir.magnitude;
                if (len < 1e-4f)
                    continue; // skip degenerate edge
                dir /= len;
                if (len > budget)
                {
                    b = a + dir * budget; // the partially drawn edge of a draw-on
                    len = budget;
                }
                budget -= len;
                var nrm = new Vector2(-dir.y, dir.x) * half; // edge normal, half-width

                AddVert(vh, a - nrm);
                AddVert(vh, a + nrm);
                AddVert(vh, b + nrm);
                AddVert(vh, b - nrm);
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vh.AddTriangle(vi + 2, vi + 3, vi);
                vi += 4;
            }
        }

        private void AddVert(VertexHelper vh, Vector2 pos)
        {
            var v = UIVertex.simpleVert;
            v.color = color;
            v.position = new Vector3(pos.x, pos.y, 0f);
            vh.AddVert(v);
        }
    }
}
