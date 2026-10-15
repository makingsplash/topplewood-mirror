using System;
using R3;
using UnityEngine;
using UnityEngine.UI;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Settings gear, top-left (TASK-562, owner's variant B; ui-composer §3.3) — the mirror twin of
    /// <see cref="RestartButtonPresenter"/>: the same code-built <c>DontDestroyOnLoad</c> overlay canvas, the same
    /// layout (<see cref="HudTopButtons"/>), the same cream die-cut sticker of the hint family, hidden while a UiService
    /// screen is up (<see cref="HudCover"/>) and while a dev menu is open (<c>RestartButtonGuard</c> finds it by name).
    ///
    /// The settings screen does not exist yet (TASK-566). A button that opens nothing is a dead button, so the gear is
    /// fully built but stays HIDDEN until someone hands it its action through <see cref="Bind"/>; TASK-566 binds it
    /// to its screen and nothing else here has to change. The EventSystem is raised by the other HUD presenters; this
    /// one is registered next to them in the prod scope only.
    /// </summary>
    public sealed class SettingsButtonPresenter : IStartable, IDisposable
    {
        public const string CanvasName = "[PlayScreen] SettingsButton";
        private const string SpritePath = "Hud/settings_button";

        private readonly PlayScreenInteraction _interaction;

        private GameObject _canvas;
        private CanvasGroup _group;
        private Button _button;
        private Action _onClick;
        private IDisposable _coverSub;

        public SettingsButtonPresenter(PlayScreenInteraction interaction)
        {
            _interaction = interaction;
        }

        public void Start()
        {
            CreateButton();
            _coverSub = _interaction.IsCoveredByScreen.Subscribe(covered => HudCover.Apply(_group, covered));
        }

        /// <summary>Gives the gear its action and shows it. Until then the gear is not on screen.</summary>
        public void Bind(Action onClick)
        {
            _onClick = onClick;
            if (_canvas != null)
                _canvas.SetActive(_onClick != null);
        }

        private void CreateButton()
        {
            _canvas = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            _group = _canvas.GetComponent<CanvasGroup>();

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Same slot as RESTART: below the DEV toggle (short.MaxValue), above the banner and the hint button.
            canvas.sortingOrder = short.MaxValue - 1;

            HudTopButtons.ConfigureScaler(_canvas.GetComponent<CanvasScaler>());

            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(SafeAreaFitter.Create(_canvas.transform), false);
            HudTopButtons.Place(buttonGo.GetComponent<RectTransform>(), right: false);

            var image = buttonGo.GetComponent<Image>();
            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
            }
            else
            {
                // Spriteless fallback keeps the button visible and tappable (text, not a ⚙ glyph the built-in font
                // may lack), same rule as RESTART.
                Debug.unityLogger.Log(LogType.Warning,
                    $"[hud] спрайт '{SpritePath}' не найден — настройки рисуются плашкой с подписью");
                image.color = new Color(0.12f, 0.12f, 0.12f, 0.7f);

                var labelGo = new GameObject("Label", typeof(Text));
                labelGo.transform.SetParent(buttonGo.transform, false);

                var label = labelGo.GetComponent<Text>();
                label.text = "SETTINGS";
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.fontSize = 26;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            _button = buttonGo.GetComponent<Button>();
            _button.onClick.AddListener(OnSettingsClicked);

            _canvas.SetActive(_onClick != null);
        }

        private void OnSettingsClicked() => _onClick?.Invoke();

        public void Dispose()
        {
            _coverSub?.Dispose();
            _coverSub = null;
            _onClick = null;

            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas);
            _canvas = null;
        }
    }
}
