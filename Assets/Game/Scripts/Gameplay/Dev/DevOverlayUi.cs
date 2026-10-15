#if GPIE_DEVELOPMENT
using System;
using Gameplay.PlayScreen;
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
        // button is still reachable. Returns the canvas GO so the caller owns its teardown. <paramref name="onClick"/> is invoked on tap.
        //
        // Look (TASK-562, ui-composer §3.2): a SERVICE chip, deliberately outside the game's style so it never reads as
        // a player button — flat translucent black, no sprite/outline/shadow, square corners, the built-in font, half
        // a fraction of the height of a game button (52 vs 200). The visible chip sits inside a larger invisible hit zone (≈57×50 dp)
        // so it stays easy to hit. It sits UNDER the top-left settings slot (HudTopButtons), which keeps that slot free
        // for the gear; the chip and the gear hit zones do not overlap.
        public static GameObject CreateToggleButton(Action onClick)
        {
            var buttonCanvas = new GameObject("[Dev] DebugMenuButton",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);

            var canvas = buttonCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above any gameplay/UI so it is never occluded.

            HudTopButtons.ConfigureScaler(buttonCanvas.GetComponent<CanvasScaler>());

            // Offsets count from the SAFE edges, like every other HUD button (TASK-539): before TASK-562 the DEV button
            // hung from the raw screen edge and sat under a cutout.
            var hitGo = new GameObject("DevHit", typeof(Image), typeof(Button));
            hitGo.transform.SetParent(SafeAreaFitter.Create(buttonCanvas.transform), false);

            var hitRect = hitGo.GetComponent<RectTransform>();
            hitRect.anchorMin = hitRect.anchorMax = hitRect.pivot = new Vector2(0f, 1f);
            hitRect.anchoredPosition = new Vector2(HudTopButtons.LeftSideInset - ChipInHit, -HudTopButtons.LeftSlotBottom);
            hitRect.sizeDelta = new Vector2(136f, 120f);

            // Alpha-0 image is the raycast target: the hit zone is invisible but tappable.
            hitGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            hitGo.GetComponent<Button>().onClick.AddListener(() => onClick());

            var chipGo = new GameObject("DevChip", typeof(Image));
            chipGo.transform.SetParent(hitGo.transform, false);
            var chipRect = chipGo.GetComponent<RectTransform>();
            chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = new Vector2(0f, 1f);
            // Visible chip on the gear's side edge, 16 units under its slot.
            chipRect.anchoredPosition = new Vector2(ChipInHit, -ChipInHit);
            chipRect.sizeDelta = new Vector2(104f, 52f);

            var chip = chipGo.GetComponent<Image>();
            chip.color = new Color(0f, 0f, 0f, 0.45f); // dev-chip-bg
            chip.raycastTarget = false;

            var labelGo = new GameObject("DevLabel", typeof(Text));
            labelGo.transform.SetParent(chipGo.transform, false);

            var label = labelGo.GetComponent<Text>();
            label.text = "DEV";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 1f, 1f, 0.8f); // dev-chip-ink
            label.fontSize = 28;
            label.raycastTarget = false;
            // Built-in font of Unity 6 (renamed from Arial.ttf pre-2022) — the system look IS the point here. If
            // unavailable the glyphs just do not draw; the chip and its click still work.
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return buttonCanvas;
        }

        // The chip's offset inside its hit zone (ui-composer §3.2: chip at (16, −16) in the zone). The zone starts at the
        // gear slot's bottom, so the chip keeps the spec's 16-unit gap under the gear and its left edge on the gear's
        // side inset — after TASK-584's round buttons that is (28, −166) for the 120-unit slot at (28, −30).
        private const float ChipInHit = 16f;
    }
}
#endif
