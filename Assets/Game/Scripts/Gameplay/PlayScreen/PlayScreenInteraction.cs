using R3;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Tiny Unity-side coordinator (TASK-116, RFC §2.4). <see cref="IsBusy"/> is true for the whole WIN/LOSE
    /// hold (from level-resolved to the end of the sequence). Written by exactly one owner —
    /// <see cref="SequencedResolutionFlow"/> — and read by <see cref="RestartButtonPresenter"/> to disable the
    /// Restart button while a resolution is playing, so a manual restart cannot race the auto-transition (Q1).
    /// </summary>
    public sealed class PlayScreenInteraction
    {
        public readonly ReactiveProperty<bool> IsBusy = new(false);
    }
}
