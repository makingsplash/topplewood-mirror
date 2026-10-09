using UnityEngine;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Hides a code-built HUD canvas while a UiService screen covers the play screen (TASK-398,
    /// <see cref="PlayScreenInteraction.IsCoveredByScreen"/>). One implementation for every HUD presenter.
    ///
    /// Why a <see cref="CanvasGroup"/> on the canvas root and not <c>SetActive</c>: the dev <c>RestartButtonGuard</c>
    /// already owns the RESTART root's active flag (hide while the debug menu is open, restore the saved state on close).
    /// A second writer on the same flag would fight it — a screen closing under an open menu would re-show RESTART over
    /// the menu's "X", and the menu closing would then restore a stale "hidden". The group is a separate lever: alpha 0
    /// removes the render, <c>blocksRaycasts = false</c> removes the hit-test (GraphicRaycaster skips graphics under a
    /// non-blocking group), so the button can be neither seen nor tapped through the screen.
    /// </summary>
    public static class HudCover
    {
        public static void Apply(CanvasGroup group, bool covered)
        {
            if (group == null)
                return;

            group.alpha = covered ? 0f : 1f;
            group.blocksRaycasts = !covered;
            group.interactable = !covered;
        }
    }
}
