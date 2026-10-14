#if GPIE_DEVELOPMENT
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Hides the product HUD button canvases — RESTART and, since TASK-539, the hint booster — for as long as a dev
    /// menu is open: the non-ordering lever from TASK-117. Extracted from <c>BringupDebugMenuHost</c> (TASK-172) so both
    /// the bringup host and the prod <see cref="GameDebugMenuEntryPoint"/> share one implementation instead of two
    /// drifting copies. The hint button sat over the menu's fields ("Add", "Input Drag Threshold") on a 20:9 phone
    /// while RESTART was already hidden (QA TASK-533 F6).
    ///
    /// Why hide the whole GameObject and not toggle <c>Canvas.enabled</c>: a disabled Canvas still leaves its
    /// graphics registered for raycasting in uGUI, so only <c>SetActive(false)</c> reliably removes a button from
    /// BOTH render and hit-testing — which is the point, since the collision is a hit-test one (RESTART at
    /// <c>short.MaxValue-1</c> swallowing the menu's close "X"). The canvases are found by their GameObject names
    /// (set in <c>RestartButtonPresenter.CreateButton</c> / <c>HintBoosterPresenter.BuildUi</c>) — a fragile,
    /// string-coupled seam, flagged here on purpose; DI injection was ruled out because exposing the canvas would mean
    /// editing the product presenters, which TASK-117 forbids. A button that was never built (menu opened outside the
    /// play-screen) is not found and silently skipped: the dev menu must open in any scene.
    /// </summary>
    public sealed class RestartButtonGuard
    {
        private static readonly string[] HudCanvasNames =
        {
            "[PlayScreen] RestartButton",
            "[PlayScreen] HintBoosterButton",
        };

        private readonly GameObject[] _hidden = new GameObject[HudCanvasNames.Length]; // what we hid; restored on close.
        private readonly bool[] _wasActive = new bool[HudCanvasNames.Length]; // activeSelf before — Restore RESTORES this.

        public void Hide()
        {
            for (var i = 0; i < HudCanvasNames.Length; i++)
            {
                _hidden[i] = GameObject.Find(HudCanvasNames[i]);
                if (_hidden[i] == null)
                    continue;

                _wasActive[i] = _hidden[i].activeSelf;
                _hidden[i].SetActive(false);
            }
        }

        // Restore each canvas to the EXACT state it had before we hid it — not an unconditional enable, so a button
        // that was already inactive for another reason (a future IsBusy/level-transition path) stays inactive (owner
        // requirement, TASK-117: restore, don't force). Cleared to null so a later Hide re-reads the live state, and so
        // a redundant Restore (already restored, or nothing hidden) is a safe no-op.
        public void Restore()
        {
            for (var i = 0; i < _hidden.Length; i++)
            {
                if (_hidden[i] == null)
                    continue;

                _hidden[i].SetActive(_wasActive[i]);
                _hidden[i] = null;
            }
        }
    }
}
#endif
