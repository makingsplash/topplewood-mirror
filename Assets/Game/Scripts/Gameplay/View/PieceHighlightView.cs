using System;
using DG.Tweening;
using Game.Services.LogService;
using Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.View
{
    /// <summary>
    /// Real <see cref="IPieceHighlightView"/> (TASK-613; the <c>PulseOutline</c> branch of the former
    /// <c>TutorialView</c>, moved as is): a code-built Screen Space Overlay that draws a BLINKING contour matching the
    /// piece's silhouette (outline, not fill, so the piece stays visible), with no finger. Context-independent — it
    /// builds its own canvas and needs no SayKit UI / addressables / Data. The overlay never eats input (raycastTarget
    /// off, no GraphicRaycaster), so the tap still reaches gameplay.
    ///
    /// The contour tracks the piece by re-projecting its world outline through <c>Camera.main</c> EVERY frame (owner
    /// requirement 2026-08-04: the camera is framed to the level on LevelLoaded, so a once-only projection could miss
    /// the first frame). The geometry is the SAME tessellation the piece mesh uses (<see cref="ShapeVisualOutline"/>,
    /// TASK-074) — no second tessellation (TASK-075). Sorting order 32000: above gameplay, below the tour (32100) and
    /// the HUD (32765+).
    /// </summary>
    public sealed class PieceHighlightView : IPieceHighlightView, IDisposable
    {
        private Overlay _overlay;

        public void Show(Vec2 origin, float rotation, Shape shape)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] PieceHighlight Show origin=({origin.X:0.##},{origin.Y:0.##}) " +
                $"rot={rotation:0.##} camMain={(UnityEngine.Camera.main != null)}");
            if (_overlay == null)
                _overlay = Overlay.Create();
            _overlay.Show(origin, rotation, shape);
        }

        public void Clear()
        {
            if (_overlay != null)
                _overlay.Hide();
        }

        public void Dispose()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject); // SetLink kills the tween with the object
            _overlay = null;
        }

        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32000;   // above gameplay + gray-box UI, below the tour overlay
            private const float OutlineWidthPx = 6f;
            private static readonly Color OutlineColor = new Color32(0x39, 0xD3, 0x53, 0xFF); // green, vertex-tinted
            // TASK-263 §2a: a BLINKING contour — alpha yoyo between full and BlinkMinAlpha.
            private const float BlinkMinAlpha = 0.2f;
            private const float BlinkHalfSec = 0.5f; // ~1s full blink cycle

            private RectTransform _anchor;     // repositioned each frame to the target's screen point
            private RectTransform _canvasRect;
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name
            private UiContour _outline;
            private Tween _blink;
            private Vector3 _worldOrigin;
            private Vec2[] _worldOutline;      // the piece silhouette in WORLD space at its pose (constant while Idle)
            private Vector2[] _screenLocal;    // scratch: outline vertices in overlay-local px, rebuilt each frame
            private bool _active;

            public static Overlay Create()
            {
                var go = new GameObject("PieceHighlightOverlay");
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

                _anchor = NewChild("Anchor", root.transform);
                // Anchored to the canvas CENTRE (the origin RectTransformUtility maps into) and collapsed to a point:
                // a default 100×100 rect put corner-anchored children 50 px below-left of the piece (TASK-078).
                _anchor.anchorMin = _anchor.anchorMax = new Vector2(0.5f, 0.5f);
                _anchor.sizeDelta = Vector2.zero;

                var outlineRt = NewChild("Contour", _anchor);
                // CanvasRenderer explicitly FIRST too (TASK-309), so correctness never rides solely on RequireComponent.
                outlineRt.gameObject.AddComponent<CanvasRenderer>();
                _outline = outlineRt.gameObject.AddComponent<UiContour>();
                _outline.color = OutlineColor;
                _outline.Width = OutlineWidthPx;
                _outline.raycastTarget = false; // never intercept the gameplay tap
            }

            public void Show(Vec2 origin, float rotation, Shape shape)
            {
                _worldOrigin = new Vector3(origin.X, origin.Y, 0f);
                // ONE source of contour geometry: the same view tessellation the piece mesh uses (TASK-074/075).
                _worldOutline = Geometry.WorldVertices(ShapeVisualOutline.Build(shape), origin, rotation);
                _screenLocal = new Vector2[_worldOutline.Length];
                _active = true;
                gameObject.SetActive(true);

                _blink?.Kill();
                SetAlpha(1f);
                // SetUpdate(true): the blink keeps going on unscaled time — a hint can be revealed while a resolution
                // hold has scaled time.
                _blink = DOTween.To(GetAlpha, SetAlpha, BlinkMinAlpha, BlinkHalfSec)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetLink(_outline.gameObject);
                Reposition(); // place correctly on the first frame too, before LateUpdate runs
            }

            public void Hide()
            {
                _blink?.Kill();
                _blink = null;
                _active = false;
                if (this != null && gameObject != null)
                    gameObject.SetActive(false);
            }

            private float GetAlpha() => _outline.color.a;

            private void SetAlpha(float a)
            {
                var c = _outline.color;
                c.a = a;
                _outline.color = c;
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
                if (_camera == null)
                    return;

                var screenOrigin = _camera.WorldToScreenPoint(_worldOrigin);
                if (screenOrigin.z < 0f)
                    return; // behind the camera — leave the highlight where it was rather than flip it

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenOrigin, null, out var local))
                    _anchor.anchoredPosition = local;

                // ConstantPixelSize at scale 1: the screen-space delta equals the overlay-local delta, so the raw pixel
                // offset IS the local mesh position.
                for (var i = 0; i < _worldOutline.Length; i++)
                {
                    var s = _camera.WorldToScreenPoint(new Vector3(_worldOutline[i].X, _worldOutline[i].Y, 0f));
                    _screenLocal[i] = new Vector2(s.x - screenOrigin.x, s.y - screenOrigin.y);
                }
                _outline.SetPoints(_screenLocal);
            }

            private static RectTransform NewChild(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = Vector2.zero; // point-anchored to parent's corner
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                return rt;
            }
        }
    }
}
