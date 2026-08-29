using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Gameplay.Configs;
using Gameplay.Core;
using Gameplay.Core.Mechanic;
using Gameplay.Tutorial;
using R3;
using Services.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Hint booster button (TASK-237) — a floating, code-built HUD button in the bottom-right, built on its own
    /// <c>DontDestroyOnLoad</c> overlay canvas exactly like <see cref="RestartButtonPresenter"/> (owner decision:
    /// code-built, not a UiBuild schema). Tapping it reveals the NEXT piece in the level's certified
    /// <c>WinningOrder</c> by REUSING the tutorial highlight seam (<see cref="ITutorialView"/>) — the booster is a
    /// pure UI hint: it never moves a piece, widens tolerance, or touches physics/state, so the sim is unaffected
    /// (architect §9). Charges are meta-state (<see cref="HintBoosterService"/>); a no-op (nothing to reveal, level
    /// ineligible, or an armed balance coalescence window) shows neutral feedback WITHOUT spending (balancer §1.2).
    ///
    /// The EventSystem is raised with the SAME idempotent guard as <see cref="RestartButtonPresenter"/> (create only
    /// if none, destroy only our own) so ownership is order-independent whichever HUD presenter starts first. The
    /// canvas sits at <c>short.MaxValue-2</c> — above the banner/tutorial highlight, below the RESTART button
    /// (<c>short.MaxValue-1</c>). The proactive A/B affordance (glow pulse + tooltip) is non-modal and never blocks
    /// the field (one-tap is sacrosanct); it is driven by <see cref="LevelFailStreak"/> under the treatment arm.
    /// </summary>
    public sealed class HintBoosterPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly HintBoosterService _hint;
        private readonly PlayScreenInteraction _interaction;
        private readonly ITutorialView _highlightView;
        private readonly LevelFailStreak _failStreak;
        private readonly IMechanicController _mechanic;

        private GameObject _eventSystem;
        private GameObject _canvas;
        private Button _button;
        private Image _icon;
        private Text _countLabel;
        private Image _glow;
        private GameObject _tooltip;
        private Tween _glowPulse;
        private CompositeDisposable _subs;
        private CancellationTokenSource _highlightCts;

        private bool _onCooldown;
        private bool _proactiveSuppressed;
        private int _suppressedAtStreak;

        public HintBoosterPresenter(
            GameModel model,
            HintBoosterService hint,
            PlayScreenInteraction interaction,
            ITutorialView highlightView,
            LevelFailStreak failStreak,
            IMechanicController mechanic)
        {
            _model = model;
            _hint = hint;
            _interaction = interaction;
            _highlightView = highlightView;
            _failStreak = failStreak;
            _mechanic = mechanic;
        }

        private BoostersConfig.HintData Cfg => _hint.HintConfig;

        public void Start()
        {
            EnsureEventSystem();
            BuildUi();

            _subs = new CompositeDisposable();
            _interaction.IsBusy.Subscribe(_ => RefreshState()).AddTo(_subs);
            _hint.Charges.Subscribe(_ => RefreshState()).AddTo(_subs);
            _failStreak.Current.Subscribe(_ => RefreshProactive()).AddTo(_subs);
            _model.LevelLoaded.Subscribe(_ =>
            {
                // New level (or reload): drop any active highlight, clear the proactive affordance, and let a
                // fresh struggle re-earn the offer.
                ClearHighlight();
                _proactiveSuppressed = false;
                _suppressedAtStreak = 0;
                RefreshState();
                RefreshProactive();
            }).AddTo(_subs);

            RefreshState();
        }

        // Idempotent EventSystem guard (mirrors RestartButtonPresenter): whoever starts first owns it.
        private void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            _eventSystem = new GameObject("[PlayScreen] EventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(_eventSystem);
        }

        private void BuildUi()
        {
            _canvas = new GameObject("[PlayScreen] HintBoosterButton",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the banner (31000) and tutorial highlight (32000), BELOW the RESTART button (short.MaxValue-1).
            canvas.sortingOrder = short.MaxValue - 2;

            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // Root button — bottom-right, thumb reach (ui-composer §2). Both top corners are taken (DEV / RESTART).
            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(_canvas.transform, false);
            var buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-40f, 200f);
            buttonRect.sizeDelta = new Vector2(150f, 150f);

            _icon = buttonGo.GetComponent<Image>();
            // Same code-load idiom as WoodArt (Resources.Load, warn-not-crash): code-built HUD has no inspector to
            // bind a sprite. Fallback to a flat accent token keeps the button visible placeholder-first.
            var sprite = Resources.Load<Sprite>("Booster/hint_button");
            if (sprite != null)
            {
                _icon.sprite = sprite;
                _icon.color = Color.white;
            }
            else
            {
                Debug.unityLogger.Log(LogType.Warning,
                    "[booster] спрайт 'Booster/hint_button' не найден — кнопка рисуется плоским токеном");
                _icon.color = new Color(0.72f, 0.52f, 0.22f, 0.95f); // warm low-chroma wood accent (not success-green).
            }

            _button = buttonGo.GetComponent<Button>();
            _button.onClick.AddListener(OnHintClicked);

            BuildGlow(buttonGo.transform);
            BuildBadge(buttonGo.transform);
            BuildTooltip();
        }

        // Proactive glow — behind the button (first sibling), wider, non-raycasting. Placeholder = warm low-chroma
        // ring (NOT success-green — art-director flag on §accent-rule); replaced by the hint_glow art slot later.
        private void BuildGlow(Transform parent)
        {
            var glowGo = new GameObject("Glow", typeof(Image));
            glowGo.transform.SetParent(parent, false);
            glowGo.transform.SetAsFirstSibling();

            var rect = glowGo.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 200f);

            _glow = glowGo.GetComponent<Image>();
            _glow.raycastTarget = false;
            _glow.color = new Color(0.86f, 0.62f, 0.24f, 0.35f);
            glowGo.SetActive(false);
        }

        // Charge badge — a dynamic count node (never baked into the art, ui-composer §2). Legacy uGUI Text, the
        // RestartButton idiom for a code-built label.
        private void BuildBadge(Transform parent)
        {
            var badgeGo = new GameObject("ChargeBadge", typeof(Image));
            badgeGo.transform.SetParent(parent, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(12f, 12f);
            badgeRect.sizeDelta = new Vector2(64f, 64f);

            var badge = badgeGo.GetComponent<Image>();
            badge.color = new Color(0.15f, 0.12f, 0.09f, 0.92f);
            badge.raycastTarget = false;

            var labelGo = new GameObject("Count", typeof(Text));
            labelGo.transform.SetParent(badgeGo.transform, false);
            _countLabel = labelGo.GetComponent<Text>();
            _countLabel.alignment = TextAnchor.MiddleCenter;
            _countLabel.color = Color.white;
            _countLabel.fontSize = 40;
            _countLabel.fontStyle = FontStyle.Bold;
            _countLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _countLabel.raycastTarget = false;

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // Non-modal tooltip above the button (right edge). blocksRaycasts=false → taps pass through to the field.
        private void BuildTooltip()
        {
            _tooltip = new GameObject("Tooltip", typeof(Image), typeof(CanvasGroup));
            _tooltip.transform.SetParent(_canvas.transform, false);
            var rect = _tooltip.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-40f, 200f + 150f + 16f);
            rect.sizeDelta = new Vector2(380f, 110f);

            _tooltip.GetComponent<Image>().color = new Color(0.15f, 0.12f, 0.09f, 0.92f);
            _tooltip.GetComponent<CanvasGroup>().blocksRaycasts = false;

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(_tooltip.transform, false);
            var label = labelGo.GetComponent<Text>();
            label.text = "Подсказка?";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 44;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.raycastTarget = false;

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 8f);
            labelRect.offsetMax = new Vector2(-16f, -8f);

            _tooltip.SetActive(false);
        }

        private bool LevelEligible()
        {
            var level = _model.CurrentLevel;
            return level != null && NextHintResolver.TapablePieceCount(level) >= Cfg.MinTapablePieces;
        }

        private void RefreshState()
        {
            if (_button == null)
                return;

            var charges = _hint.Charges.CurrentValue;
            if (_countLabel != null)
                _countLabel.text = charges.ToString();

            var interactable = charges > 0
                               && !_interaction.IsBusy.Value
                               && !_onCooldown
                               && LevelEligible();
            _button.interactable = interactable;

            // Dimming through alpha reads clearer on wood than a grey tint (ui-composer §3).
            var alpha = interactable ? 1f : 0.5f;
            if (_icon != null)
            {
                var c = _icon.color;
                c.a = alpha;
                _icon.color = c;
            }
        }

        private void RefreshProactive()
        {
            if (_glow == null)
                return;

            if (!ShouldOfferProactively())
            {
                HideProactive();
                return;
            }

            ShowProactive();
        }

        private bool ShouldOfferProactively()
        {
            if (!Cfg.ProactiveEnabled || _proactiveSuppressed)
                return false;
            if (_hint.Charges.CurrentValue <= 0 || !LevelEligible())
                return false;

            var tier = _model.CurrentLevel?.DifficultyTier ?? 0;
            var threshold = tier >= Cfg.ProactiveHardTierFloor
                ? Cfg.ProactiveFailThresholdHardTier
                : Cfg.ProactiveFailThresholdDefault;
            return _failStreak.Current.CurrentValue >= threshold;
        }

        private void ShowProactive()
        {
            if (_glow.gameObject.activeSelf)
                return;

            _glow.gameObject.SetActive(true);
            _tooltip.SetActive(true);
            _glowPulse?.Kill();
            _glowPulse = _glow.transform
                .DOScale(1.1f, 0.8f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
        }

        private void HideProactive()
        {
            _glowPulse?.Kill();
            _glowPulse = null;
            if (_glow != null)
            {
                _glow.transform.localScale = Vector3.one;
                _glow.gameObject.SetActive(false);
            }
            if (_tooltip != null)
                _tooltip.SetActive(false);
        }

        private void OnHintClicked()
        {
            // Defensive re-check (a click queued the same frame IsBusy flips), mirrors RestartButton.
            if (_interaction.IsBusy.Value || _onCooldown)
                return;

            // Balance coalescence window open → the tapped-but-unflushed piece is still Idle; revealing it again
            // would spend on an already-armed piece. Treat as a neutral no-op (architect §13 flag 2).
            if (_mechanic.HasPendingRelease)
                return;

            var instanceId = NextHintResolver.NextIdleInstanceId(_model, _model.CurrentLevel?.WinningOrder);
            if (instanceId < 0 || !LevelEligible())
                return; // nothing to reveal / ineligible → neutral feedback, NO spend (balancer §1.2).

            if (!_hint.TrySpendActivation())
                return;

            RevealPiece(instanceId);
            CooldownAsync().Forget();
        }

        private void RevealPiece(int instanceId)
        {
            PieceInstance piece = null;
            foreach (var p in _model.Pieces)
            {
                if (p.InstanceId == instanceId)
                {
                    piece = p;
                    break;
                }
            }
            if (piece == null)
                return;

            var shape = _model.TypeFor(piece).Shape;
            _highlightView.Highlight(piece.Position, piece.Rotation, shape);
            HideProactive(); // accepted: drop the affordance for this level.
            AutoClearHighlightAsync().Forget();
        }

        private async UniTaskVoid AutoClearHighlightAsync()
        {
            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = new CancellationTokenSource();
            var token = _highlightCts.Token;

            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(2.5f), cancellationToken: token);
                _highlightView.Clear();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer highlight or a level reload — that path clears the view itself.
            }
        }

        private async UniTaskVoid CooldownAsync()
        {
            _onCooldown = true;
            RefreshState();
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(Cfg.ReactivationCooldownSec));
            }
            finally
            {
                _onCooldown = false;
                RefreshState();
            }
        }

        private void ClearHighlight()
        {
            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = null;
            _highlightView.Clear();
        }

        public void Dispose()
        {
            _subs?.Dispose();
            _subs = null;

            _glowPulse?.Kill();
            _glowPulse = null;

            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = null;

            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas);
            _canvas = null;

            if (_eventSystem != null)
                UnityEngine.Object.Destroy(_eventSystem);
            _eventSystem = null;
        }
    }
}
