using System;
using UnityEngine;

namespace Services.Gameplay
{
    /// <summary>
    /// Ships-today impl of <see cref="IWinRewardDoubler"/> (TASK-240, owner decision): the ×2 button is visible but
    /// inert — a tap debug-logs and nothing else. It does NOT double the reward, does NOT call
    /// <c>onDoubled</c>, and does NOT invoke any ad (the rewarded channel is dead, TASK-127). When rewarded is
    /// revived, this class is replaced by a rewarded impl at the single bind in <c>GameplayLifetimeScope</c>; the
    /// win screen and the seam do not change.
    /// </summary>
    public sealed class StubWinRewardDoubler : IWinRewardDoubler
    {
        public void TryDouble(int reward, Action<int> onDoubled)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[TASK-240] Win ×2 button tapped — stub: reward {reward} NOT doubled, no rewarded ad (TASK-127 dead).");
        }
    }
}
