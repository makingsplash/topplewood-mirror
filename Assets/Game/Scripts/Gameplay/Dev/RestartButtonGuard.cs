#if GPIE_DEVELOPMENT
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Hides the product RESTART canvas for as long as a dev menu is open — the non-ordering lever from TASK-117.
    /// Extracted from <c>BringupDebugMenuHost</c> (TASK-172) so both the bringup host and the prod
    /// <see cref="GameDebugMenuEntryPoint"/> share one implementation instead of two drifting copies.
    ///
    /// Why hide the whole GameObject and not toggle <c>Canvas.enabled</c>: a disabled Canvas still leaves its
    /// graphics registered for raycasting in uGUI, so only <c>SetActive(false)</c> reliably removes RESTART from
    /// BOTH render and hit-testing — which is the point, since the collision is a hit-test one (RESTART at
    /// <c>short.MaxValue-1</c> swallowing the menu's close "X"). The button is found by its GameObject name
    /// ("[PlayScreen] RestartButton", set in <c>RestartButtonPresenter.CreateButton</c>) — a fragile, string-coupled
    /// seam, flagged here on purpose; DI injection was ruled out because exposing the canvas would mean editing the
    /// product presenter, which TASK-117 forbids. If the button was never built (menu opened outside the play-screen)
    /// <c>GameObject.Find</c> returns null and we silently no-op: the dev menu must open in any scene.
    /// </summary>
    public sealed class RestartButtonGuard
    {
        private GameObject _restartButton;    // product RESTART canvas GO we hide while the menu is open; restored on close.
        private bool _restartButtonWasActive; // its activeSelf before we hid it — Restore RESTORES this, never force-enables.

        public void Hide()
        {
            _restartButton = GameObject.Find("[PlayScreen] RestartButton");
            if (_restartButton == null)
                return;

            _restartButtonWasActive = _restartButton.activeSelf;
            _restartButton.SetActive(false);
        }

        // Restore the RESTART canvas to the EXACT state it had before we hid it — not an unconditional enable, so a
        // button that was already inactive for another reason (a future IsBusy/level-transition path) stays inactive
        // (owner requirement, TASK-117: restore, don't force). Cleared to null so a later Hide re-reads the live state,
        // and so a redundant Restore (already restored, or nothing hidden) is a safe no-op.
        public void Restore()
        {
            if (_restartButton == null)
                return;

            _restartButton.SetActive(_restartButtonWasActive);
            _restartButton = null;
        }
    }
}
#endif
