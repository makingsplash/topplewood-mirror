#if GPIE_DEVELOPMENT
using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gameplay.Dev
{
    /// <summary>
    /// Tap-or-drag behaviour of the DEV chip's hit zone (TASK-587). A tap opens the menu; a drag moves the chip inside
    /// its parent (the safe-area container) and does NOT open the menu; the position survives a relaunch.
    ///
    /// Why not a uGUI <c>Button</c> + drag handler on the same object: the input module still sends the click after a
    /// drag when the finger is released over the pressed object — and a dragged chip follows the finger, so it always
    /// is. The click is therefore gated by our own flag, the approach of the template's <c>DebugMenuButtonUi</c>
    /// (com.say.services.common). Unlike that button, the drag starts only past a slop (a jittery tap still opens the
    /// menu), the chip keeps its grab offset instead of jumping its centre under the finger, it is clamped to the safe
    /// area, and the position is stored as a FRACTION of the free range, so it means the same place on any screen size.
    ///
    /// The position lives in two dev-only prefs keys through the Say wrapper (persistence canon, TASK-044). Both hosts
    /// init <c>PlayerPrefsCollection</c> before they build the chip; if it is not initialised the chip still works and
    /// just does not remember where it was put.
    /// </summary>
    public sealed class DevChipDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler
    {
        // Canvas units the finger must travel before a press becomes a drag (≈10 dp on a 1080×2400 phone, the order of
        // the platform touch slop). Below it the press stays a tap.
        private const float DragSlop = 24f;

        // "Never moved": the chip sits at its default spot, recomputed for the current screen.
        private const float Unset = -1f;

        private RectTransform _rect;
        private RectTransform _area;
        private Canvas _canvas;
        private Action _onClick;
        private Vector2 _defaultInset;
        private PlayerPrefsFloat _savedX;
        private PlayerPrefsFloat _savedY;

        private Vector2 _pressPosition;
        private bool _dragged;
        private Vector2 _laidOutFor;

        /// <summary>
        /// <paramref name="defaultInset"/> = canvas units from the area's RIGHT and BOTTOM edges to the zone's
        /// right-bottom corner, used until the chip is first dragged.
        /// </summary>
        public void Init(Action onClick, Vector2 defaultInset)
        {
            _onClick = onClick;
            _defaultInset = defaultInset;
            _rect = (RectTransform)transform;
            _area = (RectTransform)_rect.parent;
            _canvas = GetComponentInParent<Canvas>().rootCanvas;

            _rect.anchorMin = _rect.anchorMax = _rect.pivot = Vector2.zero;

            if (PlayerPrefsCollection.Instance != null)
            {
                _savedX = new PlayerPrefsFloat("Dev.ChipX", Unset);
                _savedY = new PlayerPrefsFloat("Dev.ChipY", Unset);
            }

            Layout();
        }

        // The safe area may settle after start (SafeAreaFitter re-reads it every frame); keep the chip at its spot.
        private void LateUpdate()
        {
            if (!_dragged && _area.rect.size != _laidOutFor)
                Layout();
        }

        private void Layout()
        {
            var free = FreeRange(_area.rect.size, _rect.rect.size);
            var saved = _savedX == null ? new Vector2(Unset, Unset) : new Vector2(_savedX.Value, _savedY.Value);
            _rect.anchoredPosition = saved.x < 0f || saved.y < 0f
                ? Clamp(new Vector2(free.x - _defaultInset.x, _defaultInset.y), free)
                : FromFraction(saved, free);
            _laidOutFor = _area.rect.size;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragged = false;
            _pressPosition = _rect.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            var delta = (eventData.position - eventData.pressPosition) / _canvas.scaleFactor;
            if (!_dragged && delta.magnitude < DragSlop)
                return;

            _dragged = true;
            _rect.anchoredPosition = Clamp(_pressPosition + delta, FreeRange(_area.rect.size, _rect.rect.size));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragged || _savedX == null)
                return;

            var fraction = ToFraction(_rect.anchoredPosition, FreeRange(_area.rect.size, _rect.rect.size));
            _savedX.Value = fraction.x;
            _savedY.Value = fraction.y;
            // .Value only reaches the backend; flush so the spot survives a kill, not just a clean exit (canon trap №2).
            PlayerPrefsCollection.Instance.SaveDataIfChanged();
        }

        // The module sends this BEFORE OnEndDrag, so _dragged still describes the gesture that is ending.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_dragged)
                _onClick();
        }

        /// <summary>How far the zone's bottom-left corner can travel inside the area (never negative).</summary>
        public static Vector2 FreeRange(Vector2 area, Vector2 zone) =>
            new(Mathf.Max(0f, area.x - zone.x), Mathf.Max(0f, area.y - zone.y));

        public static Vector2 Clamp(Vector2 position, Vector2 free) =>
            new(Mathf.Clamp(position.x, 0f, free.x), Mathf.Clamp(position.y, 0f, free.y));

        public static Vector2 ToFraction(Vector2 position, Vector2 free) =>
            new(free.x > 0f ? Mathf.Clamp01(position.x / free.x) : 0f, free.y > 0f ? Mathf.Clamp01(position.y / free.y) : 0f);

        public static Vector2 FromFraction(Vector2 fraction, Vector2 free) =>
            new(Mathf.Clamp01(fraction.x) * free.x, Mathf.Clamp01(fraction.y) * free.y);
    }
}
#endif
