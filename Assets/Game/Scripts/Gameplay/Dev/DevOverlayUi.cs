#if GPIE_DEVELOPMENT
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Gameplay.Dev
{
    /// <summary>
    /// Scene-agnostic bootstrap for the dev cheat overlay, shared by the bringup host and the prod
    /// <see cref="GameDebugMenuEntryPoint"/> (extracted from <c>BringupDebugMenuHost</c>, TASK-172). Only the
    /// scene-independent pieces live here — the always-visible corner toggle button and the on-demand EventSystem;
    /// scene-specific concerns (which menu the button opens, sorting order, the in-menu label) stay with each host.
    /// The whole file is <c>GPIE_DEVELOPMENT</c>-guarded and never compiles into player builds.
    /// </summary>
    public static class DevOverlayUi
    {
        // A scene may ship no EventSystem (bringup uses the raw new-Input-System PointerTapInputSource, not uGUI), so
        // DDM's uGUI buttons would receive no clicks. Raise one on demand with the module for the project's active
        // input handling (Input System package → InputSystemUIInputModule; ProjectSettings activeInputHandler:1).
        // DontDestroyOnLoad so a level reload does not drop it. <paramref name="created"/> returns the GO ONLY when we
        // created it ourselves (none existed): the caller must destroy exactly that one on teardown and must never
        // touch a pre-existing EventSystem it does not own.
        public static void EnsureEventSystem(out GameObject created)
        {
            created = null;
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            created = new GameObject("[Dev] EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(created);
        }

        // The visible, keyboard-free entry point (TASK-062). The template shipped a draggable prefab button
        // (com.say.services.common DebugMenuButtonUi) instantiated by DebugService from UiPrefabs.asset, but the prefab
        // asset was cut from the repo. Rebuild the affordance in code — no prefab asset, so nothing for the Unity
        // bridge's missing prefab-save op to author. A small corner button on its own DontDestroyOnLoad overlay canvas
        // keeps the play field clear; gameplay taps ride the raw PointerTapInputSource (not uGUI), so anything under the
        // button is still reachable. Returns the canvas GO so the caller owns its teardown (and can hang extra overlay
        // widgets, e.g. the prod level indicator, on it). <paramref name="onClick"/> is invoked on tap.
        public static GameObject CreateToggleButton(Action onClick)
        {
            var buttonCanvas = new GameObject("[Dev] DebugMenuButton",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);

            var canvas = buttonCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above any gameplay/UI so it is never occluded.

            var scaler = buttonCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(buttonCanvas.transform, false);

            var image = buttonGo.GetComponent<Image>();
            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f); // top-left corner, away from the play field's centre.
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -28f);

            buttonGo.GetComponent<Button>().onClick.AddListener(() => onClick());

            // Деревянная кнопка от владельца: надпись «DEV ≡» в спрайте запечена, отдельной подписи нет.
            var sprite = Gameplay.View.WoodArt.ButtonDev;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                // Высота ОБЩАЯ для DEV и RESTART и лежит в настройках: владелец просил
                // уменьшить их в полтора раза (116 → 77) и держать одинаковыми.
                var height = Gameplay.View.WoodTuning.Current.UiButtonHeight;
                rect.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
                return buttonCanvas;
            }

            image.color = new Color(0.12f, 0.12f, 0.12f, 0.7f);
            rect.sizeDelta = new Vector2(170f, 96f);

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(buttonGo.transform, false);

            var label = labelGo.GetComponent<Text>();
            label.text = "DEV ≡";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 40;
            // Built-in font of Unity 6 (renamed from Arial.ttf pre-2022). If unavailable the glyphs just do not
            // draw — the button image and its click still work, so this is a cosmetic best-effort, not a gate.
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return buttonCanvas;
        }
    }
}
#endif
