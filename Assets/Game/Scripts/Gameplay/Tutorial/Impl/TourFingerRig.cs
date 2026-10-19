using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Controller of the tour finger component (<c>design/components/TourFinger.json</c>, TASK-613 plan §3.10): the
    /// compiler builds the layout and binds <see cref="_finger"/>/<see cref="_ripple"/>; the motion is code + <see cref="TourConfig"/>.
    /// The root's pivot IS the fingertip, so the view places the root on the touch point and mirrors it with
    /// <c>scale.x = −1</c> without the tip moving. One cycle (scenario 01 §3, ≈1.3 s, loops until the player taps):
    /// approach from up-right of the tip → press (scale about the tip, "into the screen" — what tells a tap from a
    /// swipe) → contact (a ripple ring spreads from the tip, <see cref="Contact"/> fires so the view flashes the
    /// contours in sync) → lift back → pause. Every tween is unscaled-time and linked to this GameObject.
    /// </summary>
    public sealed class TourFingerRig : MonoBehaviour
    {
        [SerializeField] private Image _finger;
        [SerializeField] private Image _ripple;

        private TourConfig _config;
        private DG.Tweening.Sequence _cycle; // qualified: the Gameplay.Sequence namespace shadows the bare name

        /// <summary>Start of each contact phase — the view flashes the piece and slot contours on it.</summary>
        public event Action Contact;

        public void Setup(TourConfig config)
        {
            _config = config;
            if (_ripple != null)
                SetRipple(0f, _config.RippleStartScale);
        }

        /// <summary>Start the cycle from the approach (a fresh beat).</summary>
        public void Play()
        {
            Stop();
            if (_finger == null || _config == null)
                return;
            var rt = _finger.rectTransform;
            var offset = Vector2.one * (_config.ApproachOffsetFraction * _config.FingerSize);
            rt.anchoredPosition = offset;
            rt.localScale = Vector3.one;

            _cycle = DOTween.Sequence()
                .AppendCallback(() =>
                {
                    rt.anchoredPosition = offset;
                    rt.localScale = Vector3.one;
                })
                .Append(rt.DOAnchorPos(Vector2.zero, _config.ApproachSeconds).SetEase(Ease.OutQuad))
                .Append(rt.DOScale(_config.PressScale, _config.PressSeconds).SetEase(Ease.InQuad))
                .AppendCallback(OnContact)
                .Append(DOVirtual.Float(0f, 1f, _config.ContactSeconds,
                    t => SetRipple(Mathf.Lerp(_config.RippleStartAlpha, 0f, t),
                        Mathf.Lerp(_config.RippleStartScale, 1f, t))).SetEase(Ease.OutQuad))
                .Append(rt.DOScale(1f, _config.LiftSeconds).SetEase(Ease.OutQuad))
                .Join(rt.DOAnchorPos(offset, _config.LiftSeconds).SetEase(Ease.OutQuad))
                .AppendInterval(_config.PauseSeconds)
                .SetLoops(-1)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        /// <summary>An out-of-turn cycle from the press (a tap went elsewhere): the finger is already on the piece at
        /// that point of the cycle. Idempotent — a second call restarts from the press again.</summary>
        public void RestartFromPress()
        {
            if (_cycle == null || !_cycle.IsActive())
                Play();
            _cycle?.Goto(_config.ApproachSeconds, true);
        }

        public void Stop()
        {
            _cycle?.Kill();
            _cycle = null;
            if (_ripple != null && _config != null)
                SetRipple(0f, _config.RippleStartScale);
        }

        private void OnContact()
        {
            Contact?.Invoke();
        }

        private void SetRipple(float alpha, float scale)
        {
            if (_ripple == null)
                return;
            var c = _ripple.color;
            c.a = alpha;
            _ripple.color = c;
            _ripple.rectTransform.localScale = Vector3.one * scale;
        }

        private void OnDestroy() => _cycle?.Kill();

        /// <summary>
        /// The rig built in code when the compiled component is missing (plan §3.10 fallback): the same layout as
        /// <c>TourFinger.json</c> — root pivot on the tip, ripple centred on the tip UNDER the finger, finger stretched
        /// with its pivot on the tip — so the tour still taps instead of silently showing nothing. The caller logs.
        /// </summary>
        public static TourFingerRig BuildFallback(RectTransform parent, TourConfig config, Sprite finger, Sprite ripple)
        {
            var size = config.FingerSize;
            var tip = FingerTipPivot;

            var root = Node("TourFinger(fallback)", parent);
            root.sizeDelta = Vector2.one * size;
            root.pivot = tip;

            var rippleRt = Node("Ripple", root);
            rippleRt.anchorMin = rippleRt.anchorMax = Vector2.zero;
            rippleRt.anchoredPosition = tip * size;
            rippleRt.sizeDelta = Vector2.one * (size * 0.75f); // 240 of 320, spec 02 §4.1
            rippleRt.gameObject.AddComponent<CanvasRenderer>();
            var rippleImage = rippleRt.gameObject.AddComponent<Image>();
            rippleImage.sprite = ripple;
            rippleImage.preserveAspect = true;
            rippleImage.raycastTarget = false;
            rippleImage.enabled = ripple != null; // no ring sprite → no white square instead of a ring

            var fingerRt = Node("Finger", root);
            fingerRt.anchorMin = Vector2.zero;
            fingerRt.anchorMax = Vector2.one;
            fingerRt.sizeDelta = Vector2.zero;
            fingerRt.pivot = tip;
            fingerRt.gameObject.AddComponent<CanvasRenderer>();
            var fingerImage = fingerRt.gameObject.AddComponent<Image>();
            fingerImage.sprite = finger;
            fingerImage.preserveAspect = true;
            fingerImage.raycastTarget = false;

            var rig = root.gameObject.AddComponent<TourFingerRig>();
            rig._finger = fingerImage;
            rig._ripple = rippleImage;
            return rig;
        }

        /// <summary>Fingertip in the sprite, UI y-up: (240, 113) of 1024 → (0.234, 0.890). Art 04 kept the redrawn finger's
        /// tip at the same point, so this pivot (and the component spec's) holds for both sprites.</summary>
        public static readonly Vector2 FingerTipPivot = new(0.234f, 0.890f);

        private static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }
    }
}
