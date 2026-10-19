using System;
using R3;
using UnityEngine;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Hides a code-built HUD canvas while a UiService screen covers the play screen (TASK-398,
    /// <see cref="PlayScreenInteraction.IsCoveredByScreen"/>), and dims it while a tutorial tour holds the focus
    /// (TASK-613, <see cref="PlayScreenInteraction.IsTourFocused"/>). One implementation for every HUD presenter: a new
    /// HUD button calls <see cref="Bind"/>, which follows BOTH flags.
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
        /// <summary>Keep <paramref name="group"/> in step with both flags of <paramref name="interaction"/> until the
        /// returned subscription is disposed — the one call a HUD presenter makes.</summary>
        public static IDisposable Bind(CanvasGroup group, PlayScreenInteraction interaction, float focusedAlpha) =>
            Observable.CombineLatest(interaction.IsCoveredByScreen, interaction.IsTourFocused,
                    (covered, focused) => (covered, focused))
                .Subscribe(s => Apply(group, s.covered, s.focused, focusedAlpha));

        /// <summary>TASK-635: the same binding plus the element's own <paramref name="hidden"/> (the Hint button before its
        /// unlock) and <paramref name="focusExempt"/> (a tour beat points AT this element, so the tour's dim must not touch
        /// it). Both live HERE so the group keeps one writer — the class <see cref="HudCover"/> already split from
        /// <c>RestartButtonGuard</c>.</summary>
        public static IDisposable Bind(CanvasGroup group, PlayScreenInteraction interaction, float focusedAlpha,
            Observable<bool> hidden, Observable<bool> focusExempt) =>
            Observable.CombineLatest(interaction.IsCoveredByScreen, interaction.IsTourFocused, hidden, focusExempt,
                    (covered, focused, h, exempt) => (covered, focused, h, exempt))
                .Subscribe(s => Apply(group, s.covered, s.h, s.focused, s.exempt, focusedAlpha));

        /// <summary><paramref name="covered"/> wins (alpha 0, as before); otherwise <paramref name="focused"/> dims the
        /// group to <paramref name="focusedAlpha"/> and makes it non-interactive — visible, so the player still knows
        /// where the buttons are, but out of the tour's way.</summary>
        public static void Apply(CanvasGroup group, bool covered, bool focused, float focusedAlpha) =>
            Apply(group, covered, false, focused, false, focusedAlpha);

        /// <summary>Full rule (TASK-635): <paramref name="covered"/> or <paramref name="hidden"/> → gone (alpha 0, no
        /// hit-test); <paramref name="focused"/> and not <paramref name="exempt"/> → dimmed and untappable; otherwise open.</summary>
        public static void Apply(CanvasGroup group, bool covered, bool hidden, bool focused, bool exempt, float focusedAlpha)
        {
            if (group == null)
                return;

            var gone = covered || hidden;
            var dimmed = !gone && focused && !exempt;
            var open = !gone && !dimmed;
            group.alpha = gone ? 0f : dimmed ? focusedAlpha : 1f;
            group.blocksRaycasts = open;
            group.interactable = open;
        }
    }
}
