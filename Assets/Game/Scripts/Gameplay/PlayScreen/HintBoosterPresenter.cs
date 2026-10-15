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
    /// At 0 charges on a usable level the button follows the f2p convention (owner TASK-263): it does NOT alpha-fade,
    /// it stays fully visible with a muted, desaturated tint and a "+" badge, and a tap buys one charge for coins
    /// (<see cref="HintBoosterService.TryPurchaseCharges"/>, TASK-240) — a short shake, never a modal, on a short balance.
    /// The art is the C-02 die-cut sticker language (owner 2026-09-01, TASK-263): an EMERALD lamp button (variant B
    /// recolour — warm off-white border + lamp glyph over a navy contour) + a round orange charge badge, non-wood —
    /// the complementary colour carries the separation from the walnut wall on its own, so the old wood-era
    /// drop-shadow crutch is gone.
    ///
    /// The EventSystem is raised with the SAME idempotent guard as <see cref="RestartButtonPresenter"/> (create only
    /// if none, destroy only our own) so ownership is order-independent whichever HUD presenter starts first. The
    /// canvas sits at <c>short.MaxValue-2</c> — above the banner/tutorial highlight, below the RESTART button
    /// (<c>short.MaxValue-1</c>); like RESTART it hides while a UiService screen is up (<see cref="HudCover"/>,
    /// TASK-398). The proactive A/B affordance (glow pulse + tooltip) is non-modal and never blocks
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
        private RectTransform _safeArea;
        private CanvasGroup _group;
        private Button _button;
        private Image _icon;
        private Text _countLabel;
        private Image _glow;
        private GameObject _tooltip;
        private Tween _glowPulse;
        private Tween _shake;
        private CompositeDisposable _subs;
        private CancellationTokenSource _highlightCts;
        // Icon tone in the READY state (white with a sprite, the flat fallback token without one), captured at build
        // so RefreshState can restore it after the empty-charge muted tint.
        private Color _iconBaseColor = Color.white;
        // InstanceId of the piece currently outlined by a hint (-1 = none). Lets the tap-driven clear (TASK-263 §2b)
        // fire the moment the PLAYER releases THAT piece, instead of waiting out the fallback timer.
        private int _activeHighlightId = -1;

        // Fallback-only: how long the blinking outline lingers if the player never taps the revealed piece. The
        // PRIMARY clear is now event-driven (PieceReleased), so this is generous — long enough to keep guiding, short
        // enough that an ignored hint eventually tidies itself up (was a hard 2.5s timer pre-TASK-263).
        private const float HighlightFallbackSec = 6f;

        // Empty-state (0 charges) icon tint — a near-neutral, muted grey MULTIPLY held at FULL alpha, so the button
        // stays clearly visible and tappable instead of fading toward invisible (owner 19:10, genre convention: at 0
        // boosters the button changes tone, it does not disappear). RETUNED for the emerald body (owner 2026-09-01
        // 20:12): the old value (0.62,0.64,0.68) had a deliberate BLUE lean tuned to reinforce the previous blue cel's
        // "cool off-blue"; multiplied onto the emerald green that same blue-heavy factor drags the hue toward teal —
        // a deliberate-looking colour, not "off". The near-neutral cool-grey below darkens/mutes the emerald almost
        // uniformly (~x0.61) so it reads "same button, dimmed = inactive, buy me" WITHOUT a hue shift, against the
        // ready state's full-colour icon + numeric badge. NOTE (unchanged, honest): Image.color is a per-channel
        // multiply — it darkens/mutes but cannot truly hue-desaturate; the art-director confirmed the die-cut sticker
        // reads muted natively (TASK-263 §6d), so the wood-era grayscale-material debt TASK-281 stays OBSOLETE and is
        // NOT re-raised. Exact tone is owner-gate-tunable on the live frame (owner reserved the visual verdict).
        private static readonly Color EmptyTint = new(0.60f, 0.61f, 0.63f, 1f);

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
            // TASK-398: hidden (render + hit-test) while a UiService screen is up — win/lose, popups.
            _interaction.IsCoveredByScreen.Subscribe(covered => HudCover.Apply(_group, covered)).AddTo(_subs);
            _hint.Charges.Subscribe(_ => RefreshState()).AddTo(_subs);
            _failStreak.Current.Subscribe(_ => RefreshProactive()).AddTo(_subs);
            // TASK-263 §2b: kill the hint outline the instant the player releases the piece it pointed at — the same
            // "piece actually left Idle" signal the tutorial uses (PieceReleased), so it is event-driven, not a timer.
            _model.PieceReleased.Subscribe(OnPieceReleased).AddTo(_subs);
            _model.LevelLoaded.Subscribe(_ =>
            {
                // New level (or reload): drop any active hint WE own, clear the proactive affordance, and let a
                // fresh struggle re-earn the offer.
                //
                // Clear the SHARED overlay only when a hint of ours is actually showing (TASK-382). This presenter
                // and TutorialRunner share ONE ITutorialView singleton, and both subscribe to LevelLoaded. R3 fans a
                // Subject out to its subscribers synchronously, in registration order, so a single LevelLoaded reaches
                // the runner FIRST — it arms the level's tour (then grand_seesaw's two fingers) — and then reaches this
                // handler. TutorialView.Clear() hides EVERY overlay, so an unconditional Clear() here tore the
                // just-armed tour down inside the same delivery: the player saw a flash, i.e. "no tutorial" on level 5
                // (owner probe 2026-09-15 proved exactly one LevelLoaded per transition, with the tour's Highlight
                // then a Hide from this handler, then his own last-subscribed probe). When no hint is showing there is
                // nothing of OURS to clear, so only cancel the fallback timer and leave the shared view — and the
                // tour armed on this same load — untouched.
                if (_activeHighlightId >= 0)
                    ClearHighlight();
                else
                    CancelHighlightTimer();
                _proactiveSuppressed = false;
                _suppressedAtStreak = 0;
                RefreshState();
                RefreshProactive();
            }).AddTo(_subs);

            RefreshState();

#if UNITY_EDITOR
            // TASK-309 render-isolation seam: let an editor menu (or the bridge, which screenshots but cannot inject
            // a tap) fire the PulseOutline highlight WITHOUT a tap, bypassing every charge/cooldown/eligibility gate,
            // so a live frame of the contour can be captured to decide class-1 (gate) vs class-2 (render).
            HintHighlightProbe.Force = ForceHighlightProbe;
#endif
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
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            _group = _canvas.GetComponent<CanvasGroup>();

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the banner (31000) and tutorial highlight (32000), BELOW the RESTART button (short.MaxValue-1).
            // Not a lever against UiService screens (Overlay beats their Camera canvas at any order) — see HudCover.
            canvas.sortingOrder = short.MaxValue - 2;

            // Layout is ONE value shared with the camera framing (HudCornerZone.HintButton, TASK-539): the framing keeps
            // the level's figure and pieces out of this corner, so the numbers must not be retyped here.
            var zone = HudCornerZone.HintButton;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(zone.RefWidth, zone.RefHeight);
            scaler.matchWidthOrHeight = zone.Match;

            // Offsets count from the SAFE edges (TASK-539, closes TASK-246): the button rides a safe-area container.
            _safeArea = SafeAreaFitter.Create(_canvas.transform);

            // Root button — bottom-right, thumb reach (ui-composer §2). Both top corners are taken (settings gear / RESTART).
            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(_safeArea, false);
            var buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-zone.SideInset, zone.EdgeInset);
            buttonRect.sizeDelta = new Vector2(zone.Width, zone.Height);

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
                _icon.color = new Color(0.18f, 0.72f, 0.55f, 0.95f); // flat emerald token echoing the recoloured die-cut button.
            }
            _iconBaseColor = _icon.color;

            _button = buttonGo.GetComponent<Button>();
            _button.onClick.AddListener(OnHintClicked);

            // No runtime drop-shadow: the C-02 die-cut language (owner 2026-09-01) carries the separation from the
            // walnut wall with complementary colour (art-director 07/08 — "отрыв несёт цвет, не дроп-шэдоу-костыль").
            // The wood-era shadow was a silhouette copy that would only muddy the clean white die-cut border here.
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

        // Charge badge — a ROUND die-cut orange backing sprite (charge_badge, C-02 language, TASK-263 §3, closes
        // TASK-248) under a dynamic count node (the number is NEVER baked into the art — the sprite face is empty,
        // ui-composer §2). Legacy uGUI Text, the RestartButton idiom for a code-built label. Falls back to a flat
        // orange token if the sprite is missing.
        private void BuildBadge(Transform parent)
        {
            var badgeGo = new GameObject("ChargeBadge", typeof(Image));
            badgeGo.transform.SetParent(parent, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(12f, 12f);
            badgeRect.sizeDelta = new Vector2(64f, 64f);

            var badge = badgeGo.GetComponent<Image>();
            var badgeSprite = Resources.Load<Sprite>("Booster/charge_badge");
            if (badgeSprite != null)
            {
                badge.sprite = badgeSprite;
                badge.color = Color.white; // the die-cut sprite carries its own orange; white keeps it un-tinted
            }
            else
            {
                Debug.unityLogger.Log(LogType.Warning,
                    "[booster] спрайт 'Booster/charge_badge' не найден — бейдж рисуется плоским токеном");
                badge.color = new Color(0.93f, 0.55f, 0.15f, 0.95f); // flat orange token echoing the C-02 die-cut badge.
            }
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
            _tooltip.transform.SetParent(_safeArea, false);
            var zone = HudCornerZone.HintButton;
            var rect = _tooltip.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-zone.SideInset, zone.EdgeInset + zone.Height + 16f);
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

        // A level is eligible only if a hint can ever be revealed on it: enough tapable pieces (balancer §2.2) AND a
        // baked WinningOrder (TASK-541). Without an order every tap is a no-op, so the button must read ineligible
        // (dimmed, non-interactable) — otherwise at 0 charges it offered a coin purchase the level can never use
        // (a level with no baked order; the case was libra_scales, which won only by a paired release and was removed
        // in TASK-549).
        private bool LevelEligible()
        {
            var level = _model.CurrentLevel;
            return level != null
                && level.WinningOrder != null && level.WinningOrder.Count > 0
                && NextHintResolver.TapablePieceCount(level) >= Cfg.MinTapablePieces;
        }

        private void RefreshState()
        {
            if (_button == null)
                return;

            var charges = _hint.Charges.CurrentValue;
            var busy = _interaction.IsBusy.Value || _onCooldown;
            var eligible = LevelEligible();

            // Empty-charge offer (owner 19:10, genre convention): on a usable level with 0 charges and no transient
            // busy/cooldown, the button does NOT fade toward invisible. It stays fully opaque with a muted grey tint,
            // the badge shows "+" instead of a count, and a tap buys a charge for coins (TASK-240). IsBusy / cooldown /
            // an ineligible level keep the previous disabled alpha-dim, unchanged.
            var buyOffer = charges == 0 && eligible && !busy;

            // Interactable when a tap does something: reveal (charges>0) or buy (empty offer). Busy/cooldown and an
            // ineligible level stay non-interactable exactly as before.
            var interactable = !busy && eligible && (charges > 0 || buyOffer);
            _button.interactable = interactable;

            if (_countLabel != null)
                _countLabel.text = buyOffer ? "+" : charges.ToString();

            if (_icon != null)
            {
                Color c;
                if (buyOffer)
                {
                    c = EmptyTint; // grey tint at FULL alpha — visible & tappable, the opposite of the old fade.
                }
                else
                {
                    // Ready → base tone at full alpha; disabled (busy/cooldown/ineligible) → the old 0.5 alpha-dim.
                    c = _iconBaseColor;
                    c.a = interactable ? 1f : 0.5f;
                }
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

        // TASK-309 diagnostic: one line per tap naming EXACTLY which branch of OnHintClicked ran, so a single live
        // tap (owner via adb, or the orchestrator) distinguishes "a gate silently swallowed the call" (class 1) from
        // "the call reached Highlight but nothing shows" (class 2 — pair with TutorialView's Highlight ENTER log).
        private static void LogHint(string msg) =>
            Debug.unityLogger.Log(LogType.Log, $"[booster] tap → {msg}");

        private void OnHintClicked()
        {
            // Defensive re-check (a click queued the same frame IsBusy flips), mirrors RestartButton.
            if (_interaction.IsBusy.Value || _onCooldown)
            {
                LogHint($"swallowed: busy={_interaction.IsBusy.Value} cooldown={_onCooldown}");
                return;
            }

            // Empty-state tap = buy a charge for coins (owner 19:10, TASK-240). The button is only interactable at 0
            // charges on an eligible level — which now includes "has a baked WinningOrder" (TASK-541) — so the bought
            // charge has an order to reveal from. Not a guarantee of a reveal: the order can already be exhausted
            // (every listed piece released), and then the next tap is a neutral no-op, still without spending.
            if (_hint.Charges.CurrentValue <= 0)
            {
                LogHint("buy-charge path (0 charges) — no highlight expected");
                TryBuyCharge();
                return;
            }

            // Co-release window open (TASK-374) → a tapped-but-unreleased group member is still Idle; revealing it
            // again would spend on a piece already being co-released. Treat as a neutral no-op (architect §13 flag 2).
            if (_mechanic.HasPendingRelease)
            {
                LogHint("swallowed: HasPendingRelease (co-release window open)");
                return;
            }

            var instanceId = NextHintResolver.NextIdleInstanceId(_model, _model.CurrentLevel?.WinningOrder);
            if (instanceId < 0 || !LevelEligible())
            {
                LogHint($"swallowed: nothing to reveal id={instanceId} eligible={LevelEligible()} " +
                        $"winningOrder={(_model.CurrentLevel?.WinningOrder != null ? "set" : "null")} " +
                        $"charges={_hint.Charges.CurrentValue}");
                return; // nothing to reveal / ineligible → neutral feedback, NO spend (balancer §1.2).
            }

            if (!_hint.TrySpendActivation())
            {
                LogHint("swallowed: TrySpendActivation=false");
                return;
            }

            LogHint($"REVEAL id={instanceId} — calling highlight seam (class-2 territory if nothing appears)");
            RevealPiece(instanceId);
            CooldownAsync().Forget();
        }

        // Empty-state buy path (owner 19:10): spend coins for a single Hint charge (TASK-240). On success the Charges
        // reactive fires → RefreshState flips the button back to the ready look (numeric badge, full colour). On a
        // short balance a brief non-blocking shake — NEVER a modal (one-tap is sacrosanct). AtCap cannot occur from 0
        // charges (0 + 1 ≤ StockCap always), so a non-Ok result here is always an affordability miss.
        private void TryBuyCharge()
        {
            var result = _hint.TryPurchaseCharges(HintBoosterService.HintSku.Single);
            if (result != HintBoosterService.PurchaseResult.Ok)
                ShakeButton();
        }

        // Short horizontal shake as non-blocking "can't afford" feedback (owner 19:10: shake/tooltip, not a modal). A
        // fresh shake completes any in-flight one first so anchoredPosition never drifts; SetUpdate(true) keeps it
        // alive under a time-scaled resolution hold.
        private void ShakeButton()
        {
            if (_button == null)
                return;

            _shake?.Kill(true);
            _shake = ((RectTransform)_button.transform)
                .DOShakeAnchorPos(0.35f, new Vector2(16f, 0f), 20, 0f)
                .SetUpdate(true);
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
            // Blinking silhouette outline, NO finger (TASK-263 §2a): a finger reads as "run the tutorial"; the hint
            // just says "this is the next piece".
            _highlightView.Highlight(piece.Position, piece.Rotation, shape, HighlightStyle.PulseOutline);
            _activeHighlightId = instanceId;
            HideProactive(); // accepted: drop the affordance for this level.
            AutoClearHighlightAsync().Forget();
        }

#if UNITY_EDITOR
        // TASK-309 render probe (editor-only): force the PulseOutline highlight past all gates. Targets the next
        // idle piece in the winning order; if the order yields nothing (level without a baked order, or all placed),
        // falls back to the first piece so a frame is ALWAYS produced. This isolates the RENDER path from the tap
        // path — if this shows a contour but a real tap does not, the defect is in a gate; if this shows nothing
        // either, the defect is in the contour render itself.
        private void ForceHighlightProbe()
        {
            var instanceId = NextHintResolver.NextIdleInstanceId(_model, _model.CurrentLevel?.WinningOrder);
            if (instanceId < 0)
            {
                foreach (var p in _model.Pieces)
                {
                    instanceId = p.InstanceId;
                    break;
                }
            }
            if (instanceId < 0)
            {
                Debug.unityLogger.Log(LogType.Warning, "[booster] probe: no piece on the field to highlight");
                return;
            }
            Debug.unityLogger.Log(LogType.Log, $"[booster] probe: forcing highlight id={instanceId} (gates bypassed)");
            RevealPiece(instanceId);
        }
#endif

        // TASK-263 §2b: the player released a piece. If it is the one the hint is pointing at, clear immediately —
        // event-driven, no timer wait. Releases of any OTHER piece leave the hint alone (it still points at its own).
        private void OnPieceReleased(int instanceId)
        {
            if (_activeHighlightId >= 0 && instanceId == _activeHighlightId)
                ClearHighlight();
        }

        private async UniTaskVoid AutoClearHighlightAsync()
        {
            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = new CancellationTokenSource();
            var token = _highlightCts.Token;

            try
            {
                // Fallback only (the tap-driven clear above is the primary path): tidy up an ignored hint.
                await UniTask.Delay(TimeSpan.FromSeconds(HighlightFallbackSec), cancellationToken: token);
                ClearHighlight();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer highlight, a tap-driven clear, or a level reload — that path clears the view.
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
            CancelHighlightTimer();
            _activeHighlightId = -1;
            _highlightView.Clear();
        }

        // Cancel the fallback auto-clear timer WITHOUT touching the shared overlay view. Used by the LevelLoaded
        // handler when no hint of ours is showing: clearing the shared view there would tear down a tutorial tour
        // armed on the same load (TASK-382), so we drop only our own pending timer.
        private void CancelHighlightTimer()
        {
            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = null;
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            if (HintHighlightProbe.Force == ForceHighlightProbe)
                HintHighlightProbe.Force = null;
#endif

            _subs?.Dispose();
            _subs = null;

            _glowPulse?.Kill();
            _glowPulse = null;

            _shake?.Kill();
            _shake = null;

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

    /// <summary>Editor-only render-isolation seam for the Hint booster (TASK-309). The live <see cref="HintBoosterPresenter"/>
    /// registers a force-highlight delegate here (in-editor only), so an editor menu item or the Unity bridge — which
    /// can screenshot a play-mode frame but cannot inject a tap — can trigger the PulseOutline highlight WITHOUT a tap
    /// and capture the contour. Player builds never populate the delegate (registration is <c>#if UNITY_EDITOR</c>);
    /// the field slot is a harmless no-cost static, kept unconditional only so the Editor assembly can reference it.</summary>
    public static class HintHighlightProbe
    {
        public static System.Action Force;
    }
}
