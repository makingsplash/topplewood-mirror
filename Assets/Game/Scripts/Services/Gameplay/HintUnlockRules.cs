using Gameplay.Core;

namespace Services.Gameplay
{
    /// <summary>What <see cref="HintUnlockRules.Decide"/> tells the caller to do on a level load (TASK-635).</summary>
    public enum HintUnlockAction
    {
        /// <summary>Nothing to change.</summary>
        None,

        /// <summary>Unlock without the tour and without touching the charges (a player already past the tour's place,
        /// or a tour level missing from the line-up); the caller also marks the tour completed.</summary>
        UnlockSilent,

        /// <summary>The tour's level, tour not yet completed: unlock and top the stock up to the intro grant.</summary>
        UnlockWithGrant,
    }

    /// <summary>
    /// The Hint unlock rule table (TASK-635 plan §3.1) — pure, Unity/Data-free, so the table is provable in EditMode
    /// (<c>HintUnlockRulesTests</c>). The tour is bound to a level by id (the catalog), "already past it" by POSITION —
    /// recomputed on every load from the tour level's CURRENT line-up index, so a re-ordered line-up never strands the
    /// button: the level moved later → the player meets it there; moved earlier → silent unlock; removed → silent unlock.
    /// First matching rule wins:
    /// <list type="number">
    /// <item>tour level not in the line-up (<c>tourLineupIndex &lt; 0</c>) → <see cref="HintUnlockAction.UnlockSilent"/> if
    /// still locked, else <see cref="HintUnlockAction.None"/>;</item>
    /// <item>passed (line-up cleared, or the campaign beyond the tour's index) and (locked or the tour not completed) →
    /// <see cref="HintUnlockAction.UnlockSilent"/> — ABOVE rule 3 on purpose: a pool/catch-up replay of the tour level
    /// must not raise the tour;</item>
    /// <item>on the tour level and the tour not completed → <see cref="HintUnlockAction.UnlockWithGrant"/> (also when
    /// already unlocked: a tour killed mid-way tops up again on the replay — never above the grant, see
    /// <see cref="TopUpAmount"/>);</item>
    /// <item>otherwise <see cref="HintUnlockAction.None"/>.</item>
    /// </list>
    /// </summary>
    public static class HintUnlockRules
    {
        public static HintUnlockAction Decide(bool unlocked, bool tourCompleted, bool onTourLevel, int tourLineupIndex,
            CampaignProgress progress)
        {
            if (tourLineupIndex < 0)
                return unlocked ? HintUnlockAction.None : HintUnlockAction.UnlockSilent;

            if (Passed(progress, tourLineupIndex) && (!unlocked || !tourCompleted))
                return HintUnlockAction.UnlockSilent;

            if (onTourLevel && !tourCompleted)
                return HintUnlockAction.UnlockWithGrant;

            return HintUnlockAction.None;
        }

        /// <summary>"The player is beyond the tour's place": the line-up was cleared once (pool / catch-up), or the
        /// first-pass position (<c>LevelNumber − 1</c>) is past <paramref name="tourLineupIndex"/>.</summary>
        public static bool Passed(CampaignProgress progress, int tourLineupIndex) =>
            progress.LineupCleared > 0 || progress.LevelNumber - 1 > tourLineupIndex;

        /// <summary>Charges to add to reach <paramref name="target"/> — a top-up TO n (<c>max(current, n)</c>), never "+n"
        /// and never a take-away.</summary>
        public static int TopUpAmount(int current, int target) => target > current ? target - current : 0;
    }
}
