using System.Collections.Generic;
using Game.Services.LogService;
using Gameplay.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>
    /// A full-canvas dim with N soft-edged cut-outs of any polygon shape (TASK-613 plan 03 §3.3, NEEDS-TOOLING T1 of
    /// spec 02) — a class, not the tour's case: a piece, the target card, the assembled figure, a HUD button, a catapult
    /// shell. Built from four sibling graphics in ONE overlay canvas; sibling order is draw order, and every pass
    /// overlaps the full-canvas quad, so uGUI's batch sorter cannot reorder them:
    /// <list type="number">
    /// <item><b>holes</b> — each cut-out offset by its pad, triangulated, writes stencil 1 with no colour;</item>
    /// <item><b>rings</b> — the soft edge, a band from pad (alpha 0) to pad+feather (alpha = dim), drawn where stencil
    /// ≠ 1 and then setting it to 1: every pixel of overlapping rings / concave corners is drawn ONCE (no double dark);</item>
    /// <item><b>dim</b> — full-canvas quad where stencil ≠ 1;</item>
    /// <item><b>reset</b> — full-canvas quad, no colour, stencil → 0, so the HUD canvases drawn after (32765+) start clean.</item>
    /// </list>
    /// Why the reference value is 1 with <c>NotEqual</c> and not the plan's "ref 0, Equal": <c>StencilMaterial.Add</c>
    /// returns the BASE material unchanged when <c>stencilID &lt;= 0</c> and the colour mask is <c>All</c>
    /// (<c>com.unity.ugui</c> <c>StencilMaterial.cs</c>), so a "ref 0" pass silently loses its stencil test and the dim
    /// would paint over the holes. Every <see cref="MaskableGraphic"/> here is <c>maskable = false</c> so a parent
    /// <c>Mask</c> can never rewrite these stencil states (plan §3.3 trap (a)).
    /// </summary>
    public sealed class SpotlightDim : MonoBehaviour
    {
        private const int StencilRef = 1;
        // Corners sharper than this are cut square (Clipper semantics, Geometry.OffsetOutward). 2 = a 60° corner
        // still mitres; a spike does not throw a vertex across the screen.
        private const float MiterLimit = 2f;
        // How far the soft ring reaches back into its hole (canvas units) to close the seam between the two meshes.
        private const float SeamOverlap = 1.5f;

        private UiPolygonFill _holes;
        private UiFeatherRing _rings;
        private Image _dim;
        private Image _reset;
        private readonly List<Material> _materials = new();

        private Color _tone = Color.black;
        private float _alpha;
        private static bool _warnedTriangulation;

        // Scratch lists reused by SetCutouts (it allocates the per-cut-out arrays only — called on change, not per frame).
        private readonly List<Vector2[]> _holePoints = new();
        private readonly List<int[]> _holeTris = new();
        private readonly List<Vector2[]> _ringInner = new();
        private readonly List<Vector2[]> _ringOuter = new();

        /// <summary>A stretched child of <paramref name="parent"/> (the overlay canvas). Points passed to
        /// <see cref="SetCutouts"/> are in the canvas's local space (origin at its centre).</summary>
        public static SpotlightDim Create(RectTransform parent, string name)
        {
            var root = Stretched(name, parent);
            var dim = root.gameObject.AddComponent<SpotlightDim>();
            dim.Build(root);
            return dim;
        }

        private void Build(RectTransform root)
        {
            var baseMat = Canvas.GetDefaultCanvasMaterial();
            _holes = Pass<UiPolygonFill>(root, "Holes",
                Stencil(baseMat, StencilOp.Replace, CompareFunction.Always, 0));
            _rings = Pass<UiFeatherRing>(root, "Rings",
                Stencil(baseMat, StencilOp.Replace, CompareFunction.NotEqual, ColorWriteMask.All));
            _dim = Pass<Image>(root, "Dim",
                Stencil(baseMat, StencilOp.Keep, CompareFunction.NotEqual, ColorWriteMask.All));
            _reset = Pass<Image>(root, "StencilReset",
                Stencil(baseMat, StencilOp.Zero, CompareFunction.Always, 0));
            Apply();
        }

        public Color Tone
        {
            get => _tone;
            set
            {
                _tone = value;
                Apply();
            }
        }

        /// <summary>Dim opacity — the one knob tweens drive; the rings' outer edge follows it.</summary>
        public float Alpha
        {
            get => _alpha;
            set
            {
                _alpha = Mathf.Clamp01(value);
                Apply();
            }
        }

        private void Apply()
        {
            if (_dim == null)
                return;
            var c = _tone;
            c.a = _alpha;
            _dim.color = c;
            _rings.color = c;
            // Nothing to dim → no draw calls at all (the stencil passes included).
            var on = _alpha > 0.001f;
            _holes.enabled = on;
            _rings.enabled = on;
            _dim.enabled = on;
            _reset.enabled = on;
        }

        /// <summary>Replace every cut-out. Allocates the offset/triangulated arrays — call it when the cut-outs CHANGE,
        /// not every frame (the tour re-projects each frame and calls only on a moved point).</summary>
        public void SetCutouts(IReadOnlyList<SpotlightCutout> cutouts)
        {
            _holePoints.Clear();
            _holeTris.Clear();
            _ringInner.Clear();
            _ringOuter.Clear();
            if (cutouts != null)
                foreach (var cut in cutouts)
                {
                    if (cut.Polygon == null || cut.Polygon.Length < 3)
                        continue;
                    var poly = ToVec2(cut.Polygon);
                    var pad = Mathf.Max(0f, cut.Pad);
                    AddHole(Geometry.OffsetOutward(poly, pad, MiterLimit));
                    if (cut.Feather > 0f)
                    {
                        // The ring starts INSIDE the hole by SeamOverlap: the hole and the ring are two meshes, and the
                        // pixels on their shared edge were covered by neither — the dim pass drew there, a dotted line
                        // of full-dim pixels along the cut-out (TASK-613 K1, measured at the hole's first outside
                        // pixel). The overlap is under stencil 1 already, so the ring never draws twice.
                        var inner = Geometry.OffsetOutward(poly, Mathf.Max(0f, pad - SeamOverlap), MiterLimit);
                        var outer = Geometry.OffsetOutward(poly, pad + cut.Feather, MiterLimit);
                        _ringInner.Add(ToVector2(inner));
                        _ringOuter.Add(ToVector2(outer));
                    }
                }
            _holes.SetPolygons(_holePoints, _holeTris);
            _rings.SetRings(_ringInner, _ringOuter);
        }

        public void ClearCutouts() => SetCutouts(null);

        private void AddHole(Vec2[] outline)
        {
            if (Geometry.TriangulateConcave(outline, out var verts, out var tris))
            {
                _holePoints.Add(ToVector2(verts));
                _holeTris.Add(tris);
                return;
            }
            // The pad offset of a concave outline can self-intersect (plan §2: harmless under the stencil, but the ear
            // clipper refuses it). A centroid fan still covers a star-shaped hole; the stencil "Replace 1" makes the
            // overlap of fan triangles harmless. Logged once, not per frame.
            if (!_warnedTriangulation)
            {
                _warnedTriangulation = true;
                Debug.unityLogger.Log(LogType.Warning,
                    $"[{LogCategory.Tutorial}] SpotlightDim: cut-out outline did not ear-clip (self-intersecting pad offset) — centroid fan used");
            }
            var n = outline.Length;
            var pts = new Vector2[n + 1];
            var centre = Vector2.zero;
            for (var i = 0; i < n; i++)
            {
                pts[i] = new Vector2(outline[i].X, outline[i].Y);
                centre += pts[i];
            }
            pts[n] = centre / n;
            var fan = new int[n * 3];
            for (var i = 0; i < n; i++)
            {
                fan[i * 3] = n;
                fan[i * 3 + 1] = i;
                fan[i * 3 + 2] = (i + 1) % n;
            }
            _holePoints.Add(pts);
            _holeTris.Add(fan);
        }

        private Material Stencil(Material baseMat, StencilOp op, CompareFunction compare, ColorWriteMask colorMask)
        {
            var mat = StencilMaterial.Add(baseMat, StencilRef, op, compare, colorMask, 255, 255);
            _materials.Add(mat);
            return mat;
        }

        private static T Pass<T>(RectTransform parent, string name, Material material) where T : MaskableGraphic
        {
            var rt = Stretched(name, parent);
            // CanvasRenderer explicitly FIRST (TASK-309), so correctness never rides solely on RequireComponent.
            rt.gameObject.AddComponent<CanvasRenderer>();
            var g = rt.gameObject.AddComponent<T>();
            g.maskable = false;
            g.raycastTarget = false;
            g.material = material;
            return g;
        }

        // Full-canvas node with a centred pivot: its local space equals the canvas's, which is what callers pass.
        private static RectTransform Stretched(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static Vec2[] ToVec2(Vector2[] src)
        {
            var dst = new Vec2[src.Length];
            for (var i = 0; i < src.Length; i++)
                dst[i] = new Vec2(src[i].x, src[i].y);
            return dst;
        }

        private static Vector2[] ToVector2(Vec2[] src)
        {
            var dst = new Vector2[src.Length];
            for (var i = 0; i < src.Length; i++)
                dst[i] = new Vector2(src[i].X, src[i].Y);
            return dst;
        }

        private void OnDestroy()
        {
            foreach (var m in _materials)
                StencilMaterial.Remove(m);
            _materials.Clear();
        }
    }
}
