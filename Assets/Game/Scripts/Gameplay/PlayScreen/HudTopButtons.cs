using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// ONE layout for the two top-corner HUD buttons — RESTART top-right and the settings gear top-left (TASK-562,
    /// variant B, ui-composer §3.1/§3.3). They are mirror twins of the hint button's family: the same 150-unit square
    /// and 40-unit side inset as <see cref="Gameplay.Core.HudCornerZone.HintButton"/>, so the right column of the HUD
    /// stands on one vertical. Both presenters read their canvas and rect from here, so the pair cannot drift apart.
    /// Units are canvas units of the shared HUD scaler (ref 1080×1920, match 0.5), measured from the SAFE top/side edge.
    /// </summary>
    public static class HudTopButtons
    {
        public const float SideInset = 40f;
        public const float TopInset = 40f;
        public const float Size = 150f;

        public static readonly Vector2 RefResolution = new(1080f, 1920f);
        public const float Match = 0.5f;

        /// <summary>Overlay HUD canvas scaler, identical to the other code-built HUD canvases.</summary>
        public static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = RefResolution;
            scaler.matchWidthOrHeight = Match;
        }

        /// <summary>Pins <paramref name="rect"/> to the top-right (<paramref name="right"/>) or top-left safe corner.</summary>
        public static void Place(RectTransform rect, bool right)
        {
            var corner = new Vector2(right ? 1f : 0f, 1f);
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = new Vector2(right ? -SideInset : SideInset, -TopInset);
            rect.sizeDelta = new Vector2(Size, Size);
        }
    }
}
