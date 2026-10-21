using Gameplay.Configs;

namespace Services.Gameplay
{
    /// <summary>The natural pause between levels at which an interstitial may be offered (TASK-658).</summary>
    public enum BetweenLevelsMoment
    {
        /// <summary>The "Continue"/"Next" tap on the win screen.</summary>
        AfterWin,

        /// <summary>The "Try again" tap on the lose screen (only with <see cref="AdsConfig.InterstitialData.OnLose"/>).</summary>
        AfterLose,
    }

    /// <summary>
    /// The between-levels interstitial rule (TASK-658 plan §3.2) — pure, Unity/SDK-free, so it is provable in EditMode
    /// (<c>InterstitialRulesTests</c>). First matching rule wins: disabled → no; a loss without <c>OnLose</c> → no;
    /// otherwise the just-played level must be ≥ <c>FromLevel</c> (so <c>FromLevel ≤ 1</c> means every level).
    /// There is deliberately NO frequency/cooldown here: the native SDK owns pacing per ad group
    /// (<c>isInterstitialAvailable</c> answers false while a group pause runs — canon <c>docs/platform/saykit.md</c>).
    /// </summary>
    public static class InterstitialRules
    {
        /// <param name="playedLevel">The 1-based shown number of the level that was JUST played (won or lost).</param>
        public static bool ShouldOffer(BetweenLevelsMoment moment, int playedLevel, AdsConfig.InterstitialData cfg)
        {
            if (cfg == null || !cfg.Enabled)
                return false;

            if (moment == BetweenLevelsMoment.AfterLose && !cfg.OnLose)
                return false;

            return playedLevel >= cfg.FromLevel;
        }
    }
}
