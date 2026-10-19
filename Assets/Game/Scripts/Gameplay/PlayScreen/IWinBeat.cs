using R3;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// The beat played on EVERY win between <c>Won</c> and the win screen (TASK-634 «В рамку», replaces the one-shot
    /// TASK-613 <c>WinInterlude</c>). Both resolution flows ask it on Won; neither knows what the beat draws. A property
    /// of the win, not an order someone places in advance — so there is no arming and no last-wins.
    /// </summary>
    public interface IWinBeat
    {
        /// <summary>
        /// True + a COLD <paramref name="beat"/>: subscribing plays it, <c>OnCompleted</c> means it is over, disposing
        /// the subscription kills it and leaves the frame where it stands. False — there is nothing to play the beat
        /// with (no card / figure / camera); the reason is already logged as a Warning and the flow keeps its plain
        /// <see cref="PlayScreenConfig.WinSettleSeconds"/> pause.
        /// </summary>
        bool TryBegin(out Observable<Unit> beat);
    }
}
