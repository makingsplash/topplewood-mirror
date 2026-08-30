using System;

namespace Services.Gameplay
{
    /// <summary>
    /// Win-screen "×2 reward" seam (TASK-240, architect §8). The button is net-new and, by owner decision, ships as
    /// a visible STUB: tapping it currently only debug-logs — it does not double the reward or show an ad. This is
    /// a seam, not an <c>if</c> in the presenter, so reviving the rewarded channel (TASK-127) is a single bind swap
    /// (<see cref="StubWinRewardDoubler"/> → a rewarded impl that shows the ad and calls back with the doubled
    /// amount) with no UI change.
    /// </summary>
    public interface IWinRewardDoubler
    {
        /// <param name="reward">The coins already credited for this win (the amount a real ×2 would add again).</param>
        /// <param name="onDoubled">Invoked with the extra amount to credit once a real rewarded view completes.
        /// The stub never calls it (nothing is doubled today).</param>
        void TryDouble(int reward, Action<int> onDoubled);
    }
}
