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
    /// green CONTOUR matching the highlighted piece's silhouette (outline, not fill, so the piece stays visible). The
    /// <see cref="HighlightStyle"/> picks the feel (TASK-263 §2): <c>FingerPoint</c> adds a tapping finger sprite
    /// over a scale-pulsing contour (FTUE tutorial); <c>PulseOutline</c> blinks the contour alpha with NO finger
    /// (Hint booster). Context-independent — it builds its own overlay Canvas and needs no
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
    /// out of that path for free. FingerPoint pulses by SCALE (owner replaced the TASK-078 alpha blink); PulseOutline
    /// blinks by ALPHA (TASK-263 §2a — owner asked for a blinking outline). The finger (FingerPoint only) is a real
    /// sprite from <c>Resources/Tutorial/tutorial_finger</c>, positioned by its off-centre TIP; it falls back to a
    /// placeholder white square (logged once) if the asset is missing so the tour never breaks silently.
    /// </summary>
    public sealed class TutorialView : ITutorialView, IDisposable
    {
        private Overlay _overlay;

        public void Highlight(Vec2 origin, float rotation, Shape shape, HighlightStyle style)
        {
            // TASK-309 diagnostic marker (owner-requested "лог-маркер на входе в Highlight"): proves the call
            // reaches the REAL view and reports the two facts that decide "draws-but-invisible" — the target pose
            // and whether Camera.main (the seam Reposition projects through) exists. A null camera means Reposition
            // early-returns forever and the contour never receives points (see Overlay.Reposition first-draw log).
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] Highlight ENTER origin=({origin.X:0.##},{origin.Y:0.##}) " +
                $"rot={rotation:0.##} style={style} camMain={(UnityEngine.Camera.main != null)}");
            (_overlay ??= Overlay.Create()).Show(origin, rotation, shape, style);
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

            // FingerPoint feel (unchanged from TASK-078).
            private const float ContourPulseScale = 1.06f;
            private const float ContourPulseHalfSec = 0.6f;
            private const float FingerTapScale = 0.78f;
            private const float FingerTapHalfSec = 0.45f;
            // PulseOutline feel (TASK-263 §2a): a BLINKING contour — alpha yoyo between full and BlinkMinAlpha.
            private const float BlinkMinAlpha = 0.2f;
            private const float BlinkHalfSec = 0.5f; // ~1s full blink cycle

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
            private Canvas _canvas;          // TASK-309: overlay Canvas component — its .enabled decides if it renders at all
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name
            private OutlineContour _outline;
            private Image _finger;           // only shown in FingerPoint style
            private Tween _contourTween;     // active contour pulse/blink — recreated per Show, killed on restyle/Hide
            private Tween _fingerTween;      // active finger tap-pulse — FingerPoint only
            private Vector3 _worldOrigin;    // the piece's pose-origin (finger target)
            private Vec2[] _worldOutline;    // the piece silhouette in WORLD space at its pose (constant while Idle)
            private Vector2[] _screenLocal;  // scratch: outline vertices in overlay-local px, rebuilt each frame
            private bool _active;
            private bool _loggedFirstDraw;   // TASK-309: one-shot diagnostic — logs the first on-screen projection per Show
            private bool _loggedReposEntry;  // TASK-309: one-shot diagnostic — logs the first Reposition BEFORE its early returns

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
                _canvas = root.GetComponent<Canvas>(); // TASK-309: kept so the first-draw log can report canvas.enabled

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

                _finger = NewImage("Finger", _anchor, FingerSpriteCache.Get(), FingerSizePx, FingerOffset);
                // The pulse/blink tweens are style-dependent (TASK-263 §2) → created per Show in ApplyStyle, not here.
            }

            // Reconfigure the finger visibility + the contour animation for the requested style. Called on every
            // Show; kills the prior tweens first so repeated hints never stack loops on the same node.
            private void ApplyStyle(HighlightStyle style)
            {
                _contourTween?.Kill();
                _fingerTween?.Kill();
                _contourTween = null;
                _fingerTween = null;

                // Reset the mutable animation state so a restyle starts from a clean baseline.
                _outline.transform.localScale = Vector3.one;
                SetOutlineAlpha(1f);
                if (_finger != null)
                    _finger.gameObject.SetActive(style == HighlightStyle.FingerPoint);

                if (style == HighlightStyle.FingerPoint)
                {
                    // Steady contour scale-pulse + finger tap-pulse (TASK-078 owner-chosen feel), looping forever.
                    _contourTween = _outline.transform.DOScale(ContourPulseScale, ContourPulseHalfSec)
                        .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(_outline.gameObject);
                    if (_finger != null)
                        _fingerTween = _finger.transform.DOScale(FingerTapScale, FingerTapHalfSec)
                            .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(_finger.gameObject);
                    return;
                }

                // PulseOutline (TASK-263 §2a): blink the contour alpha (no finger). SetUpdate(true) so the blink keeps
                // going on unscaled time — a hint can be revealed while a resolution hold has scaled time.
                _contourTween = DOTween.To(GetOutlineAlpha, SetOutlineAlpha, BlinkMinAlpha, BlinkHalfSec)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(_outline.gameObject);
            }

            private float GetOutlineAlpha() => _outline.color.a;

            private void SetOutlineAlpha(float a)
            {
                var c = _outline.color;
                c.a = a;
                _outline.color = c;
            }

            public void Show(Vec2 origin, float rotation, Shape shape, HighlightStyle style)
            {
                // TASK-309 refine (owner-requested Show/Hide trace): prove Show is actually reached and that the
                // GameObject enters the active state here. Paired with the Hide trace below, this tells "drawn but
                // torn down" (a Hide lands right after) from "drawn and left up" — the first-draw log alone can't,
                // since it fires from Reposition while active and a later Hide leaves the numbers already logged.
                Debug.unityLogger.Log(LogType.Log,
                    $"[{LogCategory.Tutorial}] Show ENTER style={style} wasActive={gameObject.activeInHierarchy}");

                _worldOrigin = new Vector3(origin.X, origin.Y, 0f);
                // ONE source of contour geometry: the same view tessellation the piece mesh uses (arcs smooth,
                // rotation baked in), transformed to world at the piece's pose. NO second tessellation (TASK-074/075).
                var local = ShapeVisualOutline.Build(shape);
                _worldOutline = Geometry.WorldVertices(local, origin, rotation);
                _screenLocal = new Vector2[_worldOutline.Length];
                _active = true;
                _loggedFirstDraw = false; // TASK-309: log where THIS highlight lands on screen
                _loggedReposEntry = false; // TASK-309: log which early return (if any) stalls THIS highlight's projection
                gameObject.SetActive(true);
                ApplyStyle(style); // finger visibility + contour pulse/blink for this consumer
                Reposition(); // place correctly on the first frame too, before LateUpdate runs
            }

            public void Hide()
            {
                // TASK-309 refine (owner-requested Show/Hide trace): if a Hide lands right after Show, the overlay
                // is deactivated with the first-draw numbers already printed — exactly the "correct numbers, zero
                // pixels" picture. The stack trace names WHO called it (Clear()/Dispose()/a coalescence path), so
                // one probe session decides it. Note Create() calls Hide() once at build time before any Show — a
                // Hide with wasActive=False and no preceding "Show ENTER" is that harmless startup call, not the bug.
                Debug.unityLogger.Log(LogType.Log,
                    $"[{LogCategory.Tutorial}] Hide called active={_active} wasActive={gameObject.activeInHierarchy}\n" +
                    UnityEngine.StackTraceUtility.ExtractStackTrace());

                _contourTween?.Kill();
                _fingerTween?.Kill();
                _contourTween = null;
                _fingerTween = null;
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
                {
                    // TASK-309 refine (owner-requested pre-return diagnostic): first-draw never printed this session while
                    // it DID in the owner's — the two early returns below are the only spots the projection can stall
                    // before first-draw. If it stalls HERE, Camera.main is null despite Highlight ENTER seeing one.
                    if (!_loggedReposEntry)
                    {
                        _loggedReposEntry = true;
                        Debug.unityLogger.Log(LogType.Log,
                            $"[{LogCategory.Tutorial}] Reposition BLOCKED cam=null (Camera.main==null) " +
                            $"screenWH=({Screen.width}x{Screen.height})");
                    }
                    return;
                }

                var screenOrigin = _camera.WorldToScreenPoint(_worldOrigin);

                // TASK-309 refine (owner-requested pre-return diagnostic): names the camera the projection ACTUALLY used
                // (name + instanceID) and the full screenOrigin INCLUDING z. If camId is not the Main Camera but the
                // overlay's UiCamera(Clone) (it sits on Main Camera in the stack and, if tagged MainCamera, Camera.main
                // can return IT), its WorldToScreenPoint yields a different z — and z<0 stalls at the return just below,
                // which is why first-draw never fires. Printed once per Show, BEFORE the z-cull, so it fires even when
                // that return does.
                if (!_loggedReposEntry)
                {
                    _loggedReposEntry = true;
                    Debug.unityLogger.Log(LogType.Log,
                        $"[{LogCategory.Tutorial}] Reposition entry cam='{_camera.name}' camId={_camera.GetInstanceID()} " +
                        $"screenOrigin=({screenOrigin.x:0.#},{screenOrigin.y:0.#},{screenOrigin.z:0.###}) " +
                        $"willCull={(screenOrigin.z < 0f)} screenWH=({Screen.width}x{Screen.height})");
                }

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

                // TASK-309 one-shot diagnostic: report the actual on-screen pixel the contour was projected to and
                // the vertex count. If this NEVER prints after a "Highlight ENTER" log, projection is stuck (camera
                // null / behind); if it prints coords OUTSIDE [0..Screen.width]×[0..Screen.height], the contour IS
                // built but off-screen (a "draws-but-invisible" cause). If it prints in-bounds coords yet nothing is
                // visible, the invisibility is in the OutlineContour render itself, not the pipeline before it.
                if (!_loggedFirstDraw)
                {
                    _loggedFirstDraw = true;
                    var onScreen = screenOrigin.x >= 0f && screenOrigin.x <= Screen.width
                                   && screenOrigin.y >= 0f && screenOrigin.y <= Screen.height;

                    // TASK-309 refine (owner-requested DECISIVE number): the px-bbox of the PROJECTED contour vertices
                    // in overlay-local px. `_screenLocal` holds this frame's outline offsets relative to the origin,
                    // so bboxW/bboxH is the contour's actual on-screen SIZE. This is what the coord log couldn't show:
                    //   bboxW/bboxH ~0 px  → geometry collapsed to a point (24 verts stacked) → invisible speck; fix
                    //                        lives in the projection (ShapeVisualOutline/WorldVertices scale).
                    //   bboxW/bboxH ~50-80 px (a piece of world-radius ~0.4 at 1080 width) BUT nothing on screen →
                    //                        geometry is fine; invisibility is in the RENDER — check the alpha/width
                    //                        printed alongside (a=0 → blink tween started at 0; WidthPx tiny → hairline).
                    var count = _screenLocal?.Length ?? 0;
                    float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
                    if (count > 0)
                    {
                        minX = maxX = _screenLocal[0].x;
                        minY = maxY = _screenLocal[0].y;
                        for (var i = 1; i < count; i++)
                        {
                            var p = _screenLocal[i];
                            if (p.x < minX) minX = p.x; else if (p.x > maxX) maxX = p.x;
                            if (p.y < minY) minY = p.y; else if (p.y > maxY) maxY = p.y;
                        }
                    }
                    var rect = _outline.rectTransform.rect;
                    // TASK-309 refine (owner-requested activeInHierarchy of canvas/contour): the render frame's
                    // live-state snapshot. Geometry (sizePx), alpha (colorA) and width proved healthy last session,
                    // yet the frame diff showed ZERO pixels — the remaining way to draw nothing with correct numbers
                    // is the object/canvas not actually rendering. canvasActive/contourActive=False → the overlay is
                    // deactivated at draw time (pair with the Show/Hide trace to see who); canvasEnabled=False → the
                    // Canvas component is off; cull=True → the CanvasRenderer is culling the mesh despite valid verts.
                    Debug.unityLogger.Log(LogType.Log,
                        $"[{LogCategory.Tutorial}] Highlight first-draw screen=({screenOrigin.x:0},{screenOrigin.y:0}) " +
                        $"onScreen={onScreen} verts={(_worldOutline?.Length ?? 0)} cam='{_camera.name}' " +
                        $"screenWH=({Screen.width}x{Screen.height}) sort={SortingOrder} " +
                        $"bboxPx=({minX:0.#}..{maxX:0.#} x {minY:0.#}..{maxY:0.#}) sizePx=({maxX - minX:0.#}x{maxY - minY:0.#}) " +
                        $"colorA={_outline.color.a:0.###} widthPx={_outline.WidthPx:0.#} " +
                        $"contourRect=({rect.width:0.#}x{rect.height:0.#}) " +
                        $"canvasActive={gameObject.activeInHierarchy} canvasEnabled={(_canvas != null && _canvas.enabled)} " +
                        $"contourActive={_outline.gameObject.activeInHierarchy} contourEnabled={_outline.enabled} " +
                        $"cull={_outline.canvasRenderer.cull}");
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
