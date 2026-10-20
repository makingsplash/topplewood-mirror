using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>
    /// Real <see cref="IPieceHighlightView"/> (TASK-613; the <c>PulseOutline</c> branch of the former
    /// <c>TutorialView</c>): a code-built Screen Space Overlay that draws a HALO around the piece's silhouette, with no
    /// finger. Context-independent — it builds its own canvas and needs no SayKit UI / addressables / Data. The overlay
    /// never eats input (raycastTarget off, no GraphicRaycaster), so the tap still reaches gameplay.
    ///
    /// TASK-641 (owner's pick of variant A «Ореол», 01-art-director): the former 6 px green contour blinking down to
    /// α 0.2 lost to the wood on LUMINANCE (WCAG 2.80, 1.69 on a green piece) and to the idle dash. Now: a solid warm
    /// light band (<see cref="BandColor"/>, offset 3…17 px OUTSIDE the silhouette — on the wall, where the contrast does
    /// not depend on the piece's colour, and over the idle dash, which it hides) + a soft glow fading outward from it
    /// that BREATHES (width and alpha) while the band never fades. Both are <see cref="UiFeatherRing"/> bands between two
    /// <see cref="Geometry.OffsetOutwardRound"/> rings (round joins as in the mock: no spike, no notch at a sharp corner —
    /// the per-edge quads of <see cref="UiContour"/> notch there, and a square-cut miter boxes the glow). The piece itself goes a little more colourful
    /// (<see cref="PieceView.SetHinted"/>), undone with the halo.
    ///
    /// The halo tracks the piece by re-projecting its world rings through <c>Camera.main</c> EVERY frame (owner
    /// requirement 2026-08-04: the camera is framed to the level on LevelLoaded, so a once-only projection could miss
    /// the first frame). The geometry is the SAME tessellation the piece mesh uses (<see cref="ShapeVisualOutline"/>,
    /// TASK-074). The px numbers are in the 1080-wide design space (match-width canvas, like the tour), so the band
    /// keeps its size against the piece on any screen. Sorting order 32000: above gameplay, below the tour (32100) and
    /// the HUD (32765+).
    /// </summary>
    public sealed class PieceHighlightView : IPieceHighlightView, IDisposable
    {
        private readonly PieceViewRegistry _views;
        private Overlay _overlay;
        private PieceView _hinted;

        public PieceHighlightView(PieceViewRegistry views) => _views = views;

        public void Show(Vec2 origin, float rotation, Shape shape, int placementIndex)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] PieceHighlight Show origin=({origin.X:0.##},{origin.Y:0.##}) " +
                $"rot={rotation:0.##} placement={placementIndex} camMain={(UnityEngine.Camera.main != null)}");
            if (_overlay == null)
                _overlay = Overlay.Create();
            _overlay.Show(origin, rotation, shape);

            Unhint();
            if (_views.TryGet(placementIndex, out var view))
            {
                view.SetHinted(true);
                _hinted = view;
            }
        }

        public void Clear()
        {
            Unhint();
            if (_overlay != null)
                _overlay.Hide();
        }

        public void Dispose()
        {
            Unhint();
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject); // SetLink kills the tween with the object
            _overlay = null;
        }

        private void Unhint()
        {
            if (_hinted != null) // a reloaded level destroys the view — Unity null, nothing to undo
                _hinted.SetHinted(false);
            _hinted = null;
        }

        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32000;   // above gameplay + gray-box UI, below the tour overlay
            private static readonly Vector2 ReferenceResolution = new(1080f, 1920f); // the tour's design space

            // Variant A numbers (01-art-director §A), design px. The band covers the idle dash (3–12 px out).
            private const float BandInnerPx = 3f;
            private const float BandOuterPx = 17f;  // 14 px band
            private static readonly Color BandColor = new Color32(0xFF, 0xF6, 0xD6, 0xFF); // α 1: never fades
            private const float GlowMinPx = 20f;    // glow width beyond the band, breathing min → max
            private const float GlowMaxPx = 32f;
            private const float GlowMinAlpha = 0.5f;
            private const float GlowMaxAlpha = 0.9f;
            private static readonly Color GlowColor = new Color32(0xFF, 0xE9, 0xA0, 0xFF);
            private const float BreathHalfSec = 0.55f; // ≈1.1 s full cycle
            private const float JoinStepRad = Mathf.PI / 12f; // round joins in ≤15° steps (the mock's linejoin=round)
            private const float RescaleEpsilon = 0.005f; // rebuild the world rings if the zoom moved by > 0.5 %

            private RectTransform _canvasRect;
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name
            private UiFeatherRing _band;
            private UiFeatherRing _glow;
            private Tween _breathTween;
            private float _breath; // 0 = glow at rest, 1 = peak

            private Vec2[] _worldOutline;      // the piece silhouette in WORLD space at its pose (constant while Idle)
            private float _builtCuPerWorld;    // the zoom the world rings below were offset for (0 = not built)
            private Vec2[] _bandInnerW, _bandOuterW, _glowMinW, _glowMaxW;
            private Vector2[] _bandInner, _bandOuter, _glowFar; // scratch: canvas-local, rebuilt each frame
            private readonly List<Vector2[]> _ringA = new() { null };
            private readonly List<Vector2[]> _ringB = new() { null };
            private bool _active;

            public static Overlay Create()
            {
                var go = new GameObject("PieceHighlightOverlay");
                DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = ReferenceResolution;
                scaler.matchWidthOrHeight = 0f; // match-width, like the tour: px numbers are design px
                // Deliberately NO GraphicRaycaster: the overlay must never intercept the gameplay tap.

                var overlay = go.AddComponent<Overlay>();
                overlay.Build(go);
                overlay.Hide();
                return overlay;
            }

            private void Build(GameObject root)
            {
                _canvasRect = (RectTransform)root.transform;
                _glow = NewRing("Glow", root.transform); // first sibling: under the band
                _glow.color = GlowColor;
                _band = NewRing("Band", root.transform);
                _band.color = BandColor;
                _band.InnerAlpha = 1f; // solid band
            }

            public void Show(Vec2 origin, float rotation, Shape shape)
            {
                // ONE source of contour geometry: the same view tessellation the piece mesh uses (TASK-074/075).
                _worldOutline = Geometry.WorldVertices(ShapeVisualOutline.Build(shape), origin, rotation);
                _builtCuPerWorld = 0f;
                _active = true;
                gameObject.SetActive(true);

                _breathTween?.Kill();
                _breath = 0f;
                // SetUpdate(true): the breath keeps going on unscaled time — a hint can be revealed while a resolution
                // hold has scaled time.
                _breathTween = DOTween.To(() => _breath, b => _breath = b, 1f, BreathHalfSec)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(gameObject);
                Reposition(); // place correctly on the first frame too, before LateUpdate runs
            }

            public void Hide()
            {
                _breathTween?.Kill();
                _breathTween = null;
                _active = false;
                if (this != null && gameObject != null)
                    gameObject.SetActive(false);
            }

            private void LateUpdate()
            {
                if (_active)
                    Reposition();
            }

            private void Reposition()
            {
                if (_camera == null)
                    _camera = UnityEngine.Camera.main; // same seam CameraFramingPresenter uses; re-fetch until one exists
                if (_camera == null || _worldOutline == null)
                    return;

                var cuPerWorld = CanvasUnitsPerWorld();
                if (cuPerWorld <= 0f)
                    return; // behind the camera — leave the halo where it was rather than flip it
                if (_builtCuPerWorld <= 0f || Mathf.Abs(cuPerWorld / _builtCuPerWorld - 1f) > RescaleEpsilon)
                    BuildWorldRings(cuPerWorld);

                if (!Project(_bandInnerW, _bandInner) || !Project(_bandOuterW, _bandOuter))
                    return;
                for (var i = 0; i < _glowFar.Length; i++)
                {
                    var a = _glowMinW[i];
                    var b = _glowMaxW[i];
                    var w = new Vector3(a.X + (b.X - a.X) * _breath, a.Y + (b.Y - a.Y) * _breath, 0f);
                    if (!OverlayProjection.TryToCanvas(_camera, _canvasRect, w, out _glowFar[i]))
                        return;
                }

                _ringA[0] = _bandInner;
                _ringB[0] = _bandOuter;
                _band.SetRings(_ringA, _ringB);
                // Glow: the FAR ring passed as inner (α 0), the band's outer edge as outer (α color.a) → fades outward.
                _ringA[0] = _glowFar;
                _glow.SetRings(_ringA, _ringB);
                var c = _glow.color;
                c.a = Mathf.Lerp(GlowMinAlpha, GlowMaxAlpha, _breath);
                _glow.color = c;
            }

            /// <summary>Offsets are taken in WORLD units (px ÷ zoom) once per zoom, not per frame: the offset allocates,
            /// and the camera is static while a piece waits. Every ring has the same vertex count (OffsetOutwardRound's
            /// arcs depend on the corner angles only), so the bands pair vertex by vertex.</summary>
            private void BuildWorldRings(float cuPerWorld)
            {
                var perPx = 1f / cuPerWorld;
                _bandInnerW = Geometry.OffsetOutwardRound(_worldOutline, BandInnerPx * perPx, JoinStepRad);
                _bandOuterW = Geometry.OffsetOutwardRound(_worldOutline, BandOuterPx * perPx, JoinStepRad);
                _glowMinW = Geometry.OffsetOutwardRound(_worldOutline, (BandOuterPx + GlowMinPx) * perPx, JoinStepRad);
                _glowMaxW = Geometry.OffsetOutwardRound(_worldOutline, (BandOuterPx + GlowMaxPx) * perPx, JoinStepRad);
                _bandInner = new Vector2[_bandInnerW.Length];
                _bandOuter = new Vector2[_bandOuterW.Length];
                _glowFar = new Vector2[Mathf.Min(_glowMinW.Length, _glowMaxW.Length)];
                _builtCuPerWorld = cuPerWorld;
            }

            private bool Project(Vec2[] world, Vector2[] local)
            {
                for (var i = 0; i < world.Length; i++)
                    if (!OverlayProjection.TryToCanvas(_camera, _canvasRect, new Vector3(world[i].X, world[i].Y, 0f), out local[i]))
                        return false;
                return true;
            }

            private float CanvasUnitsPerWorld()
            {
                if (OverlayProjection.TryToCanvas(_camera, _canvasRect, Vector3.zero, out var a)
                    && OverlayProjection.TryToCanvas(_camera, _canvasRect, Vector3.right, out var b))
                    return (b - a).magnitude;
                return 0f;
            }

            private static UiFeatherRing NewRing(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                // Centre-anchored point: OverlayProjection returns points relative to the canvas pivot (its centre).
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                // CanvasRenderer explicitly FIRST too (TASK-309), so correctness never rides solely on RequireComponent.
                go.AddComponent<CanvasRenderer>();
                var ring = go.AddComponent<UiFeatherRing>();
                ring.raycastTarget = false; // never intercept the gameplay tap
                return ring;
            }
        }
    }
}
