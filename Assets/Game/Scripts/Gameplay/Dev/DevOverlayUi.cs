#if GPIE_DEVELOPMENT
using System;
using Gameplay.Core;
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
        // bridge's missing prefab-save op to author. The button rides its own DontDestroyOnLoad overlay canvas. Returns
        // the canvas GO so the caller owns its teardown. <paramref name="onClick"/> is invoked on a tap (not on a drag).
        //
        // Look (TASK-562 ui-composer §3.2, resized TASK-587): a SERVICE chip, deliberately outside the game's style so it
        // never reads as a player button — flat opaque grey, no sprite/outline/shadow, square corners, the built-in
        // font. Owner 2026-10-16: bigger, grey, bottom-right, draggable. The chip is 168×84 (was 104×52) inside a 200×116
        // invisible hit zone (≈86×50 dp on a 1080×2400 phone); white ink on #5E5E5E measures 6.48 (contrast.py, ≥ 4.5).
        // Default spot: right edge on the hint button's right edge, a gap above the hint zone (HudCornerZone.HintButton,
        // glow pad included), so it does not cover the booster. Drag moves it anywhere in the safe area and the spot is
        // remembered (DevChipDrag).
        //
        // Every GO sits on the "UI" layer: the prod InputService drops a gameplay tap whose press STARTED over a UI-layer
        // raycast hit (IsPointerOverUIObject), so a tap or a drag on the chip never releases the piece under it. On the
        // Default layer the chip was invisible to that check (harmless in a fixed corner, not once it can be dragged
        // over the play field). The bringup PointerTapInputSource has no UI check at all — see the TASK-587 artifact.
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
            // hung from the raw screen edge and sat under a cutout. The drag is clamped to the same safe area.
            var hitGo = new GameObject("DevHit", typeof(Image), typeof(DevChipDrag));
            hitGo.transform.SetParent(SafeAreaFitter.Create(buttonCanvas.transform), false);

            var hitRect = hitGo.GetComponent<RectTransform>();
            hitRect.sizeDelta = ChipSize + 2f * new Vector2(ChipInHit, ChipInHit);

            // Alpha-0 image is the raycast target: the hit zone is invisible but tappable.
            hitGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var chipGo = new GameObject("DevChip", typeof(Image));
            chipGo.transform.SetParent(hitGo.transform, false);
            var chipRect = chipGo.GetComponent<RectTransform>();
            chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = new Vector2(0.5f, 0.5f);
            chipRect.anchoredPosition = Vector2.zero;
            chipRect.sizeDelta = ChipSize;

            var chip = chipGo.GetComponent<Image>();
            chip.color = new Color32(0x5E, 0x5E, 0x5E, 0xFF); // dev-chip-bg
            chip.raycastTarget = false;

            var labelGo = new GameObject("DevLabel", typeof(Text));
            labelGo.transform.SetParent(chipGo.transform, false);

            var label = labelGo.GetComponent<Text>();
            label.text = "DEV";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white; // dev-chip-ink
            label.fontSize = 44;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            // Built-in font of Unity 6 (renamed from Arial.ttf pre-2022) — the system look IS the point here. If
            // unavailable the glyphs just do not draw; the chip and its click still work.
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var uiLayer = LayerMask.NameToLayer("UI");
            foreach (var t in buttonCanvas.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = uiLayer;

            // Laid out last: the drag reads the zone size and the safe-area container set above.
            var hint = HudCornerZone.HintButton;
            hitGo.GetComponent<DevChipDrag>().Init(onClick, new Vector2(
                hint.SideInset - ChipInHit,
                hint.EdgeInset + hint.Height + hint.Pad + GapAboveHint - ChipInHit));

            return buttonCanvas;
        }

        private static readonly Vector2 ChipSize = new(168f, 84f);

        // The invisible margin of the hit zone around the visible chip, on every side.
        private const float ChipInHit = 16f;

        // Canvas units between the hint zone's top (button + glow pad) and the chip's bottom in the default spot.
        private const float GapAboveHint = 24f;
    }
}
#endif
