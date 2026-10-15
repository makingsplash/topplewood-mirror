using Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// The Unity side of the two top-corner HUD buttons — RESTART top-right and the settings gear top-left (TASK-562,
    /// variant B). The numbers are NOT here: they are <see cref="HudCornerZone.RestartButton"/> /
    /// <see cref="HudCornerZone.SettingsButton"/>, the same values the camera framing and the target preview keep
    /// clear (TASK-563), so the reserve cannot drift from where the buttons actually are. Size and insets follow the
    /// owner-approved mockup <c>hudtop-B-prod-01</c> (200-unit slot, 28 from the side, 30 from the top) — larger than
    /// the hint button (150/40), which the mockup did not show and stays as it was.
    /// Units are canvas units of the shared HUD scaler (ref 1080×1920, match 0.5), measured from the SAFE top/side edge.
    /// </summary>
    public static class HudTopButtons
    {
        private static HudCornerZone Zone(bool right) => right ? HudCornerZone.RestartButton : HudCornerZone.SettingsButton;

        /// <summary>Canvas units from the safe top edge to the bottom of the top-left slot — where the DEV chip starts.</summary>
        public static float LeftSlotBottom => HudCornerZone.SettingsButton.EdgeInset + HudCornerZone.SettingsButton.Height;

        /// <summary>Canvas units from the safe left edge to the top-left slot's outer edge.</summary>
        public static float LeftSideInset => HudCornerZone.SettingsButton.SideInset;

        /// <summary>Overlay HUD canvas scaler, identical to the other code-built HUD canvases.</summary>
        public static void ConfigureScaler(CanvasScaler scaler)
        {
            var zone = HudCornerZone.RestartButton;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(zone.RefWidth, zone.RefHeight);
            scaler.matchWidthOrHeight = zone.Match;
        }

        /// <summary>Pins <paramref name="rect"/> to the top-right (<paramref name="right"/>) or top-left safe corner.</summary>
        public static void Place(RectTransform rect, bool right)
        {
            var zone = Zone(right);
            var corner = new Vector2(right ? 1f : 0f, 1f);
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = new Vector2(right ? -zone.SideInset : zone.SideInset, -zone.EdgeInset);
            rect.sizeDelta = new Vector2(zone.Width, zone.Height);
        }
    }
}
