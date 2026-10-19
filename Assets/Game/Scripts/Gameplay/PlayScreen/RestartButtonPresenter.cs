using System;
using Cysharp.Threading.Tasks;
using R3;
using Services;
using Ui;
using Ui.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Always-on-screen Restart button, top-right (TASK-116, RFC §4). Built in code on its own
    /// <c>DontDestroyOnLoad</c> overlay canvas (survives level reload). uGUI needs an EventSystem, but gameplay
    /// taps ride the raw <c>PointerTapInputSource</c> (not uGUI), so we raise one with the SAME idempotent guard
    /// as <c>BringupDebugMenuHost.EnsureEventSystem</c>: create only if none exists, and in <see cref="Dispose"/>
    /// destroy ONLY the one we created (<c>_eventSystem</c> non-null ⇔ we made it). Ownership is therefore
    /// order-independent by construction — whoever starts first owns it, the other finds it ready — so this does
    /// NOT rely on registration order relative to the dev DDM host (RFC gate note, TASK-116). In player builds the
    /// DDM host is not compiled, and this is the sole EventSystem owner.
    ///
    /// The button is disabled while <see cref="PlayScreenInteraction.IsBusy"/> (the WIN/LOSE hold), so a restart
    /// cannot race the auto-transition (Q1). It is HIDDEN while a UiService screen is up
    /// (<see cref="PlayScreenInteraction.IsCoveredByScreen"/>, TASK-398) — including its own confirm popup. Tapping it opens the confirm popup (TASK-153) rather than reloading
    /// directly; confirming reloads the current level via <see cref="ICampaignRestart"/> without resolving a loss,
    /// so restart stays neutral for the domain (Q3).
    /// </summary>
    public sealed class RestartButtonPresenter : IStartable, IDisposable
    {
        private const string SpritePath = "Hud/restart_button";

        private readonly ICampaignRestart _restart;
        private readonly PlayScreenInteraction _interaction;
        private readonly Gameplay.Tutorial.TourConfig _tour;

        private GameObject _eventSystem;
        private GameObject _canvas;
        private CanvasGroup _group;
        private Button _button;
        private IDisposable _busySub;
        private IDisposable _coverSub;

        public RestartButtonPresenter(ICampaignRestart restart, PlayScreenInteraction interaction,
            Gameplay.Tutorial.TourConfig tour)
        {
            _restart = restart;
            _interaction = interaction;
            _tour = tour;
        }

        public void Start()
        {
            EnsureEventSystem();
            CreateButton();
            _busySub = _interaction.IsBusy.Subscribe(busy =>
            {
                if (_button != null)
                    _button.interactable = !busy;
            });
            // Hidden under a UiService screen (TASK-398), dimmed and untappable during a tour (TASK-613).
            _coverSub = HudCover.Bind(_group, _interaction, _tour.HudFocusedAlpha);
        }

        // Idempotent EventSystem guard (see class summary). DontDestroyOnLoad so a level reload does not drop it.
        private void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            _eventSystem = new GameObject("[PlayScreen] EventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(_eventSystem);
        }

        private void CreateButton()
        {
            _canvas = new GameObject("[PlayScreen] RestartButton",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            _group = _canvas.GetComponent<CanvasGroup>();

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the banner (31000); below any topmost dev overlay. NOT a lever against UiService screens: Overlay
            // draws above their Screen Space Camera canvas whatever this number is — hence HudCover (TASK-398).
            canvas.sortingOrder = short.MaxValue - 1;

            HudTopButtons.ConfigureScaler(_canvas.GetComponent<CanvasScaler>());

            // Offsets count from the SAFE edges (TASK-539, closes TASK-246): under a cutout the button stays tappable.
            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(SafeAreaFitter.Create(_canvas.transform), false);

            var image = buttonGo.GetComponent<Image>();
            var rect = buttonGo.GetComponent<RectTransform>();
            // Top-right, 120-unit round slot per the approved mockup — ONE layout value with the settings gear and the
            // camera framing's top-corner reserve (HudCornerZone.RestartButton via HudTopButtons, TASK-562/563/584).
            HudTopButtons.Place(rect, right: true);

            // Кремовый die-cut стикер семейства подсказки, но КРУГЛЫЙ (TASK-584, вариант A; STYLE_LOCK §hud-accent):
            // форма отличает системную кнопку от бустера. Глиф ↺ — часть арта, текста в спрайте нет. Спрайт целиком,
            // в пропорции: 9-slice не нужен. Тап ловит весь квадрат слота — альфа-хит-тест не включать.
            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
            }
            else
            {
                // Спрайт не найден — но кнопка обязана быть: тёмная плашка с подписью. Текстом, а не глифом ↺:
                // во встроенном шрифте его может не оказаться (ui-composer §3.1).
                Debug.unityLogger.Log(LogType.Warning,
                    $"[hud] спрайт '{SpritePath}' не найден — RESTART рисуется плашкой с подписью");
                image.color = new Color(0.12f, 0.12f, 0.12f, 0.7f);

                var labelGo = new GameObject("Label", typeof(Text));
                labelGo.transform.SetParent(buttonGo.transform, false);

                var label = labelGo.GetComponent<Text>();
                label.text = "RESTART";
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.fontSize = 20; // fits the 120-unit slot (TASK-584)
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            _button = buttonGo.GetComponent<Button>();
            _button.onClick.AddListener(OnRestartClicked);
        }

        private void OnRestartClicked()
        {
            // interactable already tracks !IsBusy; re-check defensively so the "no transition race" invariant
            // holds even for a click queued the same frame IsBusy flips true — and so the confirm popup never
            // opens while an auto-transition is already underway (TASK-153, owner trap: no popup over a running
            // reload).
            if (_interaction.IsBusy.Value)
                return;
            ShowRestartConfirmAsync().Forget();
        }

        // TASK-153: RESTART no longer reloads directly — it opens the pre-built confirm popup and hands
        // ReloadCurrent as the confirmation action. Cancel needs no callback: RestartPopup.OnClickCancel/Submit
        // both Close() themselves, and cancel is a pure close with no side effects. A second tap while the popup
        // is open does NOT stack a second popup — UiService keys shown screens by name and Show<T> returns the
        // already-shown controller (or the in-progress task) instead of instantiating again (UiService
        // _shownScreens / _screensInProgress), so re-Setup on the same instance is a harmless re-assign.
        // The confirm callback re-checks IsBusy at the moment ReloadCurrent actually runs, so a transition that
        // began while the popup was open still cannot be raced (preserves the TASK-116 invariant at the call site).
        private async UniTaskVoid ShowRestartConfirmAsync()
        {
            var popup = await Service.UI.Show<RestartPopup>(ScreenNames.RestartPopup);
            popup.Setup(
                onClickCancelRestart: null,
                onClickConfirmRestart: () =>
                {
                    if (_interaction.IsBusy.Value)
                        return;
                    _restart.ReloadCurrent();
                });
        }

        public void Dispose()
        {
            _busySub?.Dispose();
            _busySub = null;
            _coverSub?.Dispose();
            _coverSub = null;

            // Both canvases are DontDestroyOnLoad, so tear them down explicitly (mirrors BringupDebugMenuHost).
            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas);
            _canvas = null;

            // Destroy the EventSystem ONLY if we created it (non-null ⇔ we did); never touch someone else's.
            if (_eventSystem != null)
                UnityEngine.Object.Destroy(_eventSystem);
            _eventSystem = null;
        }
    }
}
