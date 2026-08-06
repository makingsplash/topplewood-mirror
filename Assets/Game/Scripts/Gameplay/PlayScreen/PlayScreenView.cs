using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Procedural WIN/LOSE banner (TASK-116, RFC §4). A plain C# class (not a scene MonoBehaviour) that builds its
    /// own <see cref="RenderMode.ScreenSpaceOverlay"/> canvas in code, exactly like <c>TutorialView</c> and
    /// <c>BringupDebugMenuHost.CreateToggleButton</c> — the bringup scene has no Canvas and deliberately does not
    /// raise the SayKit UI graph (no prefab / <c>ILogService</c> for <c>UIShowHide</c>, RFC §4), so the ready-made
    /// UI animators are unusable here. Animation is procedural DOTween (scale pop-in via <c>OutBack</c> + alpha
    /// fade), the idiom already established by <c>TutorialView</c>. The overlay carries NO GraphicRaycaster, so it
    /// never intercepts the gameplay tap or the Restart button. <c>DontDestroyOnLoad</c> so it survives a level
    /// reload; <see cref="Dispose"/> destroys the GO (SetLink kills the tween with it).
    /// </summary>
    public sealed class PlayScreenView : IPlayScreenView, IDisposable
    {
        // Above gameplay; below the tutorial overlay (32000) and the Restart button (short.MaxValue-1) so the
        // button stays on top and tappable while the banner is up.
        private const int SortingOrder = 31000;

        private readonly PlayScreenConfig _config;

        private GameObject _root;
        private CanvasGroup _group;
        private RectTransform _bannerRect;
        private Text _label;

        public PlayScreenView(PlayScreenConfig config) => _config = config;

        public async UniTask ShowOutcomeAsync(GameOutcome outcome, CancellationToken ct)
        {
            EnsureBuilt();

            _label.text = outcome == GameOutcome.Won ? "WIN" : "LOSE";
            _label.color = outcome == GameOutcome.Won ? _config.WinColor : _config.LoseColor;

            _root.SetActive(true);
            _group.alpha = 0f;
            _bannerRect.localScale = Vector3.one * 0.6f;

            // Pop-in (scale, OutBack) + fade, both procedural core DOTween (no UGUI-module dependency for the
            // fade: DOTween.To drives CanvasGroup.alpha directly). SetLink kills the tween if the GO dies.
            var intro = DOTween.Sequence()
                .Append(_bannerRect.DOScale(1f, _config.BannerIntroSeconds).SetEase(Ease.OutBack))
                .Join(DOTween.To(() => _group.alpha, a => _group.alpha = a, 1f, _config.BannerIntroSeconds))
                .SetLink(_root);

            // Cancelling ct throws OperationCanceledException; the awaiting sequence element .Forget()s it, so
            // Finished never fires and the transition is (correctly) NOT run on teardown.
            await intro.ToUniTask(cancellationToken: ct);
        }

        public void HideOutcome()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (_root != null)
                return;

            _root = new GameObject("[PlayScreen] OutcomeBanner",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // Deliberately NO GraphicRaycaster: the banner must never intercept the gameplay tap or the button.

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            _group = _root.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(_root.transform, false);

            _label = labelGo.GetComponent<Text>();
            _label.alignment = TextAnchor.MiddleCenter;
            _label.fontSize = 220;
            _label.fontStyle = FontStyle.Bold;
            _label.raycastTarget = false;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            // Built-in font of Unity 6 (renamed from Arial.ttf pre-2022), same as BringupDebugMenuHost — no TMP
            // assumption. If unavailable the glyphs just do not draw; the animation/timing still run.
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _bannerRect = (RectTransform)labelGo.transform;
            _bannerRect.anchorMin = _bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            _bannerRect.pivot = new Vector2(0.5f, 0.5f);
            _bannerRect.anchoredPosition = Vector2.zero;
            _bannerRect.sizeDelta = new Vector2(1000f, 400f);

            _root.SetActive(false);
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root); // SetLink kills the tween with the GO
            _root = null;
        }
    }
}
