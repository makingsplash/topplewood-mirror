using System;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Real <see cref="ITutorialView"/> (TASK-078 §1, TASK-083): a code-built screen-space overlay that draws a
    /// green CONTOUR matching the highlighted piece's silhouette (outline, not fill, so the piece stays visible)
    /// plus a finger sprite that taps it. Context-independent — it builds its own overlay Canvas and needs no
    /// SayKit UI / addressables / Data, so it works in both prod and bringup (swapping the logging stub for it is
    /// a one-line installer change). The overlay does NOT eat input (raycastTarget off, no GraphicRaycaster), so
    /// the tap still reaches gameplay.
    ///
    /// The contour + finger track the piece by re-projecting its world outline through <c>Camera.main</c> EVERY
    /// frame (owner requirement, 2026-08-04): the gameplay camera is framed to the level on LevelLoaded, so a
    /// once-only projection could land the highlight off the piece on the first frame; per-frame projection also
    /// survives orientation / resolution changes and does not depend on init order.
    ///
    /// "Above all pieces" is guaranteed by the overlay's <c>ScreenSpaceOverlay</c> render mode at
    /// <see cref="Overlay.SortingOrder"/> = 32000 (composited after the camera, outside scene depth) — no
    /// render-state / z-bias reasoning needed. The contour geometry is the SAME tessellation the piece MESH uses
    /// (<see cref="ShapeVisualOutline"/>, TASK-074) projected orthographically; there is no second tessellation
    /// (the source-divergence defect TASK-075 guards against). Arc primitives (half-disc wheels) and rotation come
    /// out of that path for free. Pulse is by SCALE (owner replaced the TASK-078 alpha blink). The finger is a real
    /// sprite from <c>Resources/Tutorial/tutorial_finger</c>, positioned by its off-centre TIP; it falls back to a
    /// placeholder white square (logged once) if the asset is missing so the tour never breaks silently.
    /// </summary>
    public sealed class TutorialView : ITutorialView, IDisposable
    {
        private Overlay _overlay;

        public void Highlight(Vec2 origin, float rotation, Shape shape)
        {
            (_overlay ??= Overlay.Create()).Show(origin, rotation, shape);
        }

        public void Clear() => _overlay?.Hide();

        public void Dispose()
        {
            if (_overlay != null)
                _overlay.Destroy();
            _overlay = null;
        }

        /// <summary>MonoBehaviour driver on the overlay GameObject: owns the contour/finger, tweens, and the
        /// per-frame world→screen re-projection. Lives on its own GO so it can tick in <see cref="LateUpdate"/>.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32000;   // above gameplay + gray-box UI
            private const float FingerSizePx = 160f;
            private const float OutlineWidthPx = 6f;
            private static readonly Color OutlineColor = new Color32(0x39, 0xD3, 0x53, 0xFF); // green, vertex-tinted

            // The finger sprite's TIP is off-centre: measured (−0.266, −0.390) of the sprite size from its centre
            // in image space (y-down, owner 2026-08-04). To land the tip on the target, offset the sprite centre by
            // (+0.266, −0.390)·size in UI space (y-up) so the fingertip points at the piece and the hand falls
            // down-right of it. Kept as fractions of FingerSizePx so the size stays a single knob.
            private const float FingerTipFracX = 0.266f;
            private const float FingerTipFracY = 0.390f;
            private static readonly Vector2 FingerOffset =
                new(FingerTipFracX * FingerSizePx, -FingerTipFracY * FingerSizePx);

            private RectTransform _anchor;   // repositioned each frame to the target's screen point
            private RectTransform _canvasRect; // overlay canvas rect — reference space for the screen→UI conversion
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name
            private OutlineContour _outline;
            private Vector3 _worldOrigin;    // the piece's pose-origin (finger target)
            private Vec2[] _worldOutline;    // the piece silhouette in WORLD space at its pose (constant while Idle)
            private Vector2[] _screenLocal;  // scratch: outline vertices in overlay-local px, rebuilt each frame
            private bool _active;

            public static Overlay Create()
            {
                var go = new GameObject("TutorialOverlay");
                DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 1:1 with WorldToScreenPoint pixels
                scaler.scaleFactor = 1f;
                // Deliberately NO GraphicRaycaster: the overlay must never intercept the gameplay tap.

                var overlay = go.AddComponent<Overlay>();
                overlay.Build(go);
                overlay.Hide();
                return overlay;
            }

            private void Build(GameObject root)
            {
                _canvasRect = (RectTransform)root.transform;

                _anchor = NewChild("Anchor", root.transform, Vector2.zero);
                // Anchor to the canvas CENTRE: Reposition() maps the target's screen point into this canvas's own
                // local space via RectTransformUtility, whose origin is the rect pivot (centre). Anchoring the node
                // to that same centre makes anchoredPosition == that local point.
                _anchor.anchorMin = _anchor.anchorMax = new Vector2(0.5f, 0.5f);
                // MUST force zero size: a fresh RectTransform defaults to sizeDelta (100,100), and the contour/finger
                // children are corner-anchored (anchorMin/Max 0,0), so their pivots would sit at this node's rect
                // BOTTOM-LEFT = anchoredPosition − (50,50) — a constant offset that put the highlight 50px below-left
                // of the piece (TASK-078 editor repro: worldCenter (371,874) vs target screen (421,924) = −(50,50),
                // exactly half the default rect). Collapsing the rect to a point makes bottom-left == centre, so each
                // child's offset (0 for the contour, FingerOffset for the finger) reads from the target itself.
                _anchor.sizeDelta = Vector2.zero;

                // Contour: a green shape-matching outline. Its node sits AT the target (offset 0), and its mesh
                // vertices are the projected silhouette in px RELATIVE to the target, so localScale pulses the
                // contour around the piece rather than around the canvas centre.
                var outlineRt = NewChild("Contour", _anchor, Vector2.zero);
                _outline = outlineRt.gameObject.AddComponent<OutlineContour>();
                _outline.color = OutlineColor;
                _outline.WidthPx = OutlineWidthPx;
                _outline.raycastTarget = false; // never intercept the gameplay tap

                var finger = NewImage("Finger", _anchor, FingerSpriteCache.Get(), FingerSizePx, FingerOffset);

                // Contour: gentle pulse (scale), looping forever. Small amplitude so it does not spoil aiming.
                _outline.transform.DOScale(1.06f, 0.6f)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(_outline.gameObject);

                // Finger: tap pulse (scale down then back), looping forever.
                finger.transform.DOScale(0.78f, 0.45f)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(finger.gameObject);
            }

            public void Show(Vec2 origin, float rotation, Shape shape)
            {
                _worldOrigin = new Vector3(origin.X, origin.Y, 0f);
                // ONE source of contour geometry: the same view tessellation the piece mesh uses (arcs smooth,
                // rotation baked in), transformed to world at the piece's pose. NO second tessellation (TASK-074/075).
                var local = ShapeVisualOutline.Build(shape);
                _worldOutline = Geometry.WorldVertices(local, origin, rotation);
                _screenLocal = new Vector2[_worldOutline.Length];
                _active = true;
                gameObject.SetActive(true);
                Reposition(); // place correctly on the first frame too, before LateUpdate runs
            }

            public void Hide()
            {
                _active = false;
                if (this != null && gameObject != null)
                    gameObject.SetActive(false);
            }

            public void Destroy()
            {
                if (this != null && gameObject != null)
                    UnityEngine.Object.Destroy(gameObject); // SetLink kills the tweens with the objects
            }

            // Re-project every frame: the camera is framed to the level on LevelLoaded, so a once-only projection
            // can miss on the first frame; this also rides out orientation / resolution changes for free.
            private void LateUpdate()
            {
                if (_active)
                    Reposition();
            }

            private void Reposition()
            {
                if (_camera == null)
                    _camera = UnityEngine.Camera.main; // same seam CameraFramingPresenter uses; re-fetch until one exists
                if (_camera == null)
                    return;

                var screenOrigin = _camera.WorldToScreenPoint(_worldOrigin);
                if (screenOrigin.z < 0f)
                    return; // behind the camera — leave the highlight where it was rather than flip it

                // Screen pixels → this canvas's local space. For a ScreenSpaceOverlay canvas at scaleFactor 1 this is
                // just (screen − canvasCentre), but going through the utility keeps it exact if the canvas ever gains
                // a cutout/DPI/scale factor.
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvasRect, screenOrigin, null, out var local))
                    _anchor.anchoredPosition = local;

                // The ortho camera makes projection affine, so the contour is exactly the piece's on-screen
                // silhouette. Each vertex goes to px RELATIVE to the origin: at scaleFactor 1 the screen-space delta
                // equals the overlay-local delta (the RectTransformUtility translation cancels in a difference), so
                // the raw pixel offset IS the local mesh position — no per-vertex utility call needed.
                if (_worldOutline != null && _screenLocal != null)
                {
                    for (var i = 0; i < _worldOutline.Length; i++)
                    {
                        var w = new Vector3(_worldOutline[i].X, _worldOutline[i].Y, 0f);
                        var s = _camera.WorldToScreenPoint(w);
                        _screenLocal[i] = new Vector2(s.x - screenOrigin.x, s.y - screenOrigin.y);
                    }
                    _outline.SetPoints(_screenLocal);
                }
            }

            private static RectTransform NewChild(string name, Transform parent, Vector2 anchoredPos)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = Vector2.zero; // point-anchored to parent's corner; anchoredPosition = local px offset
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = anchoredPos;
                return rt;
            }

            private static Image NewImage(string name, Transform parent, Sprite sprite, float sizePx, Vector2 offset)
            {
                var rt = NewChild(name, parent, offset);
                rt.sizeDelta = new Vector2(sizePx, sizePx);
                var image = rt.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false; // never intercept the gameplay tap
                return image;
            }
        }

        /// <summary>A closed constant-width stroke tracing a set of screen-local points. UI-mesh only (vertex-color
        /// tinted default shader, no material) — the piece material <c>BlockMaterial</c> is not involved. Corners
        /// use per-edge quads that may overlap slightly; acceptable for a highlight (TASK-083 §3.2).</summary>
        private sealed class OutlineContour : MaskableGraphic
        {
            private Vector2[] _points;

            public float WidthPx { get; set; } = 6f;

            // No sprite/texture: the base Graphic.mainTexture returns the built-in white texture, tinted by the
            // green vertex colour via the default UI shader — no material of our own, BlockMaterial untouched.

            public void SetPoints(Vector2[] points)
            {
                _points = points;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (_points == null || _points.Length < 2)
                    return;

                var half = WidthPx * 0.5f;
                var n = _points.Length;
                var vi = 0;
                for (var i = 0; i < n; i++)
                {
                    var a = _points[i];
                    var b = _points[(i + 1) % n];
                    var dir = b - a;
                    var len = dir.magnitude;
                    if (len < 1e-4f)
                        continue; // skip degenerate edge
                    dir /= len;
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

        /// <summary>Lazily-loaded, cached finger sprite (<c>Resources/Tutorial/tutorial_finger</c>, TASK-083). Falls
        /// back to a placeholder white square — logged ONCE — if the asset is missing, so the tour keeps working
        /// (bringup and prod resolve content differently, and a silently-dropped highlight is exactly the failure
        /// class this fallback guards against).</summary>
        private static class FingerSpriteCache
        {
            private static Sprite _sprite;
            private static bool _loaded;

            public static Sprite Get()
            {
                if (_loaded)
                    return _sprite;
                _loaded = true;

                _sprite = Resources.Load<Sprite>("Tutorial/tutorial_finger");
                if (_sprite == null)
                {
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[{LogCategory.Tutorial}] finger sprite 'Tutorial/tutorial_finger' not found — using placeholder square");
                    _sprite = WhiteSpriteCache.Get();
                }
                return _sprite;
            }
        }

        /// <summary>Lazily-built, cached solid-white square sprite — the finger fallback when the real sprite is
        /// missing (TASK-083).</summary>
        private static class WhiteSpriteCache
        {
            private static Sprite _sprite;

            public static Sprite Get()
            {
                if (_sprite != null)
                    return _sprite;

                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();

                _sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                return _sprite;
            }
        }
    }
}
