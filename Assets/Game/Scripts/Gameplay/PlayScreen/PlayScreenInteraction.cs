using R3;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Tiny Unity-side coordinator (TASK-116, RFC §2.4). <see cref="IsBusy"/> is true for the whole WIN/LOSE
    /// hold (from level-resolved to the end of the sequence / until the panel tap). Written by the active
    /// level-resolution flow — prod <see cref="GatedResolutionFlow"/> (since TASK-157), bringup
    /// <see cref="SequencedResolutionFlow"/> — and read by <see cref="RestartButtonPresenter"/> to disable the
    /// Restart button while a resolution is playing, so a manual restart cannot race the auto-transition (Q1).
    /// TASK-113 reads the same flag from <c>GameDebugMenuRestartBridge</c> to skip a dev reset while a panel is up.
    ///
    /// <see cref="IsCoveredByScreen"/> (TASK-398) is true while ANY UiService screen/popup is up (win, lose, restart
    /// confirm, message…). Written by <see cref="HudScreenCoverBridge"/> (prod scope only — bringup shows no UiService
    /// screens, so there it stays false) and read by the code-built HUD presenters to HIDE their buttons: those live on
    /// Screen Space Overlay canvases, and Overlay always draws above the Screen Space Camera UiService canvas whatever
    /// the sortingOrder, so without this the HUD sat on top of the screen and stayed tappable through it.
    ///
    /// <see cref="IsTourFocused"/> (TASK-613) is true while a tutorial tour runs. Written by
    /// <c>TutorialRunner</c> (true on start, false on completion and on stop) and read by the same HUD presenters
    /// through <see cref="HudCover.Apply"/>: the buttons are dimmed and stop
    /// taking taps, so the tour's dim cannot be bypassed and the hint is not a second writer next to the tour.
    /// </summary>
    public sealed class PlayScreenInteraction
    {
        public readonly ReactiveProperty<bool> IsBusy = new(false);
        public readonly ReactiveProperty<bool> IsCoveredByScreen = new(false);
        public readonly ReactiveProperty<bool> IsTourFocused = new(false);
    }
}
