using System;
using DG.Tweening;
using Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Real <see cref="ITutorialView"/> (TASK-078 §1): a code-built screen-space overlay that draws a softly
    /// blinking ring around the highlighted piece and a "finger" that taps it. Context-independent — it builds
    /// its own overlay Canvas and needs no SayKit UI / addressables / Data, so it works in both prod and bringup
    /// (swapping the logging stub for it is a one-line installer change). The overlay does NOT eat input
    /// (raycastTarget off, no GraphicRaycaster), so the tap still reaches gameplay.
    ///
    /// The ring + finger track the piece by re-projecting the world target through <c>Camera.main</c> EVERY frame
    /// (owner requirement, 2026-08-04): the gameplay camera is framed to the level on LevelLoaded, so a
    /// once-only projection could land the highlight off the piece on the first frame; per-frame projection also
    /// survives orientation / resolution changes and does not depend on init order. Art is placeholder (fixed-
    /// radius ring + white-square finger, owner directive 3) — real sprites are a content follow-up.
    /// </summary>
    public sealed class TutorialView : ITutorialView, IDisposable
    {
        private Overlay _overlay;

        public void Highlight(Vec2 worldTarget)
        {
            (_overlay ??= Overlay.Create()).Show(new Vector3(worldTarget.X, worldTarget.Y, 0f));
        }

        public void Clear() => _overlay?.Hide();

        public void Dispose()
        {
            if (_overlay != null)
                _overlay.Destroy();
            _overlay = null;
        }

        /// <summary>MonoBehaviour driver on the overlay GameObject: owns the ring/finger, tweens, and the per-frame
        /// world→screen re-projection. Lives on its own GO so it can tick in <see cref="LateUpdate"/>.</summary>
        private sealed class Overlay : MonoBehaviour
        {
            private const int SortingOrder = 32000;   // above gameplay + gray-box UI
            private const float RingSizePx = 220f;
            private const float FingerSizePx = 90f;
            private static readonly Vector2 FingerOffset = new(38f, -60f); // below-right of centre, like a real finger

            private RectTransform _anchor;   // repositioned each frame to the target's screen point
            private RectTransform _canvasRect; // overlay canvas rect — reference space for the screen→UI conversion
            private UnityEngine.Camera _camera; // fully-qualified: Gameplay.Camera namespace shadows the bare name
            private Vector3 _worldTarget;
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
                // to that same centre makes anchoredPosition == that local point. (The ring/finger children keep
                // their corner anchor: _anchor is zero-size, so their offsets read identically from any corner.)
                _anchor.anchorMin = _anchor.anchorMax = new Vector2(0.5f, 0.5f);

                var ring = NewImage("Ring", _anchor, RingSpriteCache.Get(), RingSizePx, Vector2.zero);
                var finger = NewImage("Finger", _anchor, WhiteSpriteCache.Get(), FingerSizePx, FingerOffset);

                // Ring: soft blink (alpha) + gentle pulse (scale), looping forever.
                var ringGroup = ring.gameObject.AddComponent<CanvasGroup>();
                DOTween.To(() => ringGroup.alpha, a => ringGroup.alpha = a, 0.35f, 0.6f)
                    .From(1f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(ring.gameObject);
                ring.transform.DOScale(1.12f, 0.6f)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(ring.gameObject);

                // Finger: tap pulse (scale down then back), looping forever.
                finger.transform.DOScale(0.78f, 0.45f)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(finger.gameObject);
            }

            public void Show(Vector3 worldTarget)
            {
                _worldTarget = worldTarget;
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

                var screen = _camera.WorldToScreenPoint(_worldTarget);
                if (screen.z < 0f)
                    return; // behind the camera — leave the ring where it was rather than flip it

                // Convert the screen point into the overlay canvas's OWN local space instead of assuming screen(0,0)
                // maps to the canvas rect's bottom-left at scaleFactor 1. That assumption held in the editor but
                // drifted by a constant on-device (TASK-078 device pass: ring off the piece by ~(-50,+53) px on a
                // display with a cutout): the real render surface / safe area shifts the canvas rect's origin, so the
                // raw pixel value lives in a different coordinate system than anchoredPosition. RectTransformUtility
                // maps through the canvas's actual world corners, so it stays exact under cutout / DPI / orientation.
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvasRect, screen, null, out var local))
                    _anchor.anchoredPosition = local;
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

        /// <summary>Lazily-built, cached ring sprite (transparent centre, opaque band). Placeholder art.</summary>
        private static class RingSpriteCache
        {
            private static Sprite _sprite;

            public static Sprite Get()
            {
                if (_sprite != null)
                    return _sprite;

                const int size = 128;
                const float center = (size - 1) * 0.5f;
                const float outer = 60f;
                const float inner = 50f;

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    // 1px feather on both edges so the band is not aliased.
                    var a = Mathf.Clamp01(Mathf.Min(d - inner, outer - d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                _sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _sprite;
            }
        }

        /// <summary>Lazily-built, cached solid-white square sprite for the placeholder finger (owner directive 3).</summary>
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
