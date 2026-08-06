using System;
using R3;
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
    /// cannot race the auto-transition (Q1). Restart is neutral for the domain (Q3): it reloads the current level
    /// via <see cref="ICampaignRestart"/> without resolving a loss.
    /// </summary>
    public sealed class RestartButtonPresenter : IStartable, IDisposable
    {
        private readonly ICampaignRestart _restart;
        private readonly PlayScreenInteraction _interaction;

        private GameObject _eventSystem;
        private GameObject _canvas;
        private Button _button;
        private IDisposable _busySub;

        public RestartButtonPresenter(ICampaignRestart restart, PlayScreenInteraction interaction)
        {
            _restart = restart;
            _interaction = interaction;
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
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1; // above the banner (31000); below any topmost dev overlay.

            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(_canvas.transform, false);
            buttonGo.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.7f);

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f); // top-right corner (owner requirement).
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-28f, -28f);
            rect.sizeDelta = new Vector2(230f, 96f);

            _button = buttonGo.GetComponent<Button>();
            _button.onClick.AddListener(OnRestartClicked);

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(buttonGo.transform, false);

            var label = labelGo.GetComponent<Text>();
            label.text = "RESTART";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 34;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private void OnRestartClicked()
        {
            // interactable already tracks !IsBusy; re-check defensively so the "no transition race" invariant
            // holds even for a click queued the same frame IsBusy flips true.
            if (_interaction.IsBusy.Value)
                return;
            _restart.ReloadCurrent();
        }

        public void Dispose()
        {
            _busySub?.Dispose();
            _busySub = null;

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
