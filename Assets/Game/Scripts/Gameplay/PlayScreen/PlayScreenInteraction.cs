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
    /// </summary>
    public sealed class PlayScreenInteraction
    {
        public readonly ReactiveProperty<bool> IsBusy = new(false);
    }
}
