using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>The soft edge of a <see cref="SpotlightDim"/> cut-out (TASK-613, T1): for each ring, a band between an
    /// INNER outline (vertex alpha 0) and an OUTER outline of the same vertex count (vertex alpha = <c>color.a</c>), one
    /// quad per edge. A stencil can only cut a hard edge ("stencils do not support alpha or softness"), so the softness
    /// lives here, in vertex alpha — the plan 03 §2 choice over a soft-mask package. Inner/outer are the same outline
    /// offset by pad and pad+feather (<c>Geometry.OffsetOutward</c> picks miter vs square per corner independently of
    /// the distance, so both offsets have the same topology).
    ///
    /// <c>[RequireComponent(typeof(CanvasRenderer))]</c> is mandatory on the concrete type (TASK-309).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiFeatherRing : MaskableGraphic
    {
        private readonly List<Vector2[]> _inner = new();
        private readonly List<Vector2[]> _outer = new();

        /// <summary>Replace every ring. A pair whose counts differ is skipped (no band rather than a twisted one).</summary>
        public void SetRings(IReadOnlyList<Vector2[]> inner, IReadOnlyList<Vector2[]> outer)
        {
            _inner.Clear();
            _outer.Clear();
            if (inner != null && outer != null)
                for (var i = 0; i < inner.Count && i < outer.Count; i++)
                {
                    if (inner[i] == null || outer[i] == null || inner[i].Length != outer[i].Length || inner[i].Length < 3)
                        continue;
                    _inner.Add(inner[i]);
                    _outer.Add(outer[i]);
                }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 solid = color;
            var clear = solid;
            clear.a = 0;
            var vi = 0;
            for (var r = 0; r < _inner.Count; r++)
            {
                var a = _inner[r];
                var b = _outer[r];
                var n = a.Length;
                for (var i = 0; i < n; i++)
                {
                    var j = (i + 1) % n;
                    AddVert(vh, a[i], clear);
                    AddVert(vh, a[j], clear);
                    AddVert(vh, b[j], solid);
                    AddVert(vh, b[i], solid);
                    vh.AddTriangle(vi, vi + 1, vi + 2);
                    vh.AddTriangle(vi + 2, vi + 3, vi);
                    vi += 4;
                }
            }
        }

        private static void AddVert(VertexHelper vh, Vector2 pos, Color32 c)
        {
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = new Vector3(pos.x, pos.y, 0f);
            vh.AddVert(v);
        }
    }
}
