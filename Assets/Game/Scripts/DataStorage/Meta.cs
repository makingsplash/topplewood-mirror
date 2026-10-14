using System;
using SGS.Data;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;
using Game.Services.LogService;

namespace DataStorage
{
    public class Meta
    {
        // 1-based level counter, increments on every level win — keeps growing past the line-up into the pool
        // (TASK-528: "Level 81, 82…"), so it is the shown number, not a line-up position
        public PlayerPrefsInt CurrentLevel => SayData.Meta.CurrentLevel;
        public readonly PlayerPrefsInt CurrentLevelRestarts = new ("Meta.CurrentLevelRestarts", 0);
        public readonly PlayerPrefsInt CurrentLevelRevives = new ("Meta.CurrentLevelRevives", 0);
        public readonly PlayerPrefsInt CurrentLevelRewardedRevives = new ("Meta.CurrentLevelRewardedRevives", 0);

        // Mirror of CurrentLevel − 1, kept coherent by MetaCampaignStore. NOT a line-up index after the line-up
        // (TASK-528): in the pool the loaded level comes from PoolCursor. No reader in game code.
        public PlayerPrefsInt CurrentLevelIndex => SayData.Meta.CurrentLevelIndex;

        /// <summary>
        /// SDK-canon set of completed tutorial ids. To modify, use the
        /// <see cref="IsTutorialCompleted"/> / <see cref="MarkTutorialCompleted"/> / <see cref="ResetTutorial"/>
        /// methods below (TASK-049) — they are the "TutorialsDataManager" the old comment referenced, which never
        /// existed as a class. Kept internal: the tutorial layer reaches this only through ITutorialProgressStore.
        /// </summary>
        internal PlayerPrefsListString CompletedTutorials => SayData.Meta.CompletedTutorials;

        public readonly PlayerPrefsBool RateUsShown = new ("Meta.RateUsShown", false);
        public readonly PlayerPrefsBool FreeTutorialReviveUsed = new ("Meta.FreeTutorialReviveUsed", false);
        public readonly PlayerPrefsInt GoldAmount = new ("Meta.GoldAmount", 0);
        public readonly PlayerPrefsInt HeartsAmount = new ("Meta.HeartsAmount", 1);
        public readonly PlayerPrefsDateTime LastHeartsAddTime = new("Meta.LastHeartsAddTime", DateTime.MinValue);
        public readonly PlayerPrefsDateTime InfiniteHeartsExpireDate = new("Meta.InfiniteHeartsExpireDate", DateTime.MinValue);
        public readonly PlayerPrefsDateTime FirstStartDate = new("Meta.FirstStartDate", DateTime.MinValue);
        public readonly PlayerPrefsDateTime LastRewardedShownTime = new ("Meta.LastRewardedShownTime", DateTime.MinValue);

        public readonly PlayerPrefsInt LevelRewardBackupAmount = new ("Meta.LevelRewardBackupAmount", 0);

        // TASK-237: Hint booster charges — meta-state beside Gold/Hearts (survive an app restart, NOT in-level).
        // Additive PlayerPrefs keys: a missing key defaults (0/false/MinValue), so the one-time seed still fires on
        // an upgrading install — no schema bump, no migration (architect §8).
        public readonly PlayerPrefsInt HintCharges = new ("Meta.HintCharges", 0);
        public readonly PlayerPrefsBool HintSeeded = new ("Meta.HintSeeded", false);
        public readonly PlayerPrefsDateTime HintLastDailyRefill = new ("Meta.HintLastDailyRefill", DateTime.MinValue);

        // TASK-240: coin economy — additive PlayerPrefs keys (a missing key defaults, so the one-time seed still
        // fires on an upgrading install; no schema bump, no migration — architect §9).
        // StartGoldSeeded guards the one-time start-gold grant (EconomySeedService).
        public readonly PlayerPrefsBool StartGoldSeeded = new ("Meta.StartGoldSeeded", false);
        // TASK-528: level pool after the line-up — additive keys (a missing key defaults to 0 = first pass, so an
        // upgrading install behaves as before; no schema bump, no migration). LineupCleared > 0 is the persisted
        // "campaign completed" fact (closes TASK-158); PoolCursor is the next place in the pool circle.
        public readonly PlayerPrefsInt LineupCleared = new ("Meta.LineupCleared", 0);
        public readonly PlayerPrefsInt PoolCursor = new ("Meta.PoolCursor", 0);
        // TASK-432: the first-clear set "Meta.RewardedLevelIds" is gone with the gate it served (every win pays now).
        // Installs that had it keep an orphan PlayerPrefs key nothing reads — harmless, no migration needed.

        public bool ForceShowHomeScreen = false;
        public bool ForceSkipHomeScreen = false;

        public readonly RewardedStateData RewardedState = new();

        public readonly PlayerPrefsDictionary<int, string> LevelsProgress = new("Meta.LevelsProgress");
        public readonly PlayerPrefsDictionary<int, string> LevelsRevives = new("Meta.AdaptiveDifficultyReviveLevels");
        public readonly PlayerPrefsInt DifficultyAfterLoseLevelIndex = new("AdaptiveDifficulty.DifficultyAfterLoseLevelIndex", -1);

        private const string LevelReviveMarkerValue = "1";

        public DateTime LastInterstitial
        {
            get => SayData.Meta.LastInterstitial;
            set => SayData.Meta.LastInterstitial = value;
        }

        public void ClearProgress(int levelIndex)
        {
            var levelProgress = LevelsProgress.Value;
            if (levelProgress != null)
            {
                if (levelProgress.ContainsKey(levelIndex))
                {
                    levelProgress.Remove(levelIndex);
                    LevelsProgress.ForceSave();
                    Debug.unityLogger.Log(LogType.Log, $"[{LogCategory.Save}] Progress for levelIndex {levelIndex} cleared");
                }
            }
        }

        public bool HasReviveInLevel(int levelIndex)
        {
            var levelsRevives = LevelsRevives.Value;
            return levelsRevives != null && levelsRevives.ContainsKey(levelIndex);
        }

        public void MarkReviveInLevel(int levelIndex)
        {
            var levelsRevives = LevelsRevives.Value;
            if (levelsRevives == null || levelsRevives.ContainsKey(levelIndex))
            {
                return;
            }

            levelsRevives[levelIndex] = LevelReviveMarkerValue;
            LevelsRevives.ForceSave();
        }

        // Tutorial completion accessors (TASK-049): the canon "TutorialsDataManager" the CompletedTutorials
        // comment referenced. All writes go through the SHARED CompletedTutorials instance and flush durably so
        // completion survives a kill (persistence R1 + explicit flush). Reached only via ITutorialProgressStore.
        public bool IsTutorialCompleted(string tutorialId)
        {
            var completed = CompletedTutorials.Value;
            return completed != null && completed.Contains(tutorialId);
        }

        public void MarkTutorialCompleted(string tutorialId)
        {
            if (IsTutorialCompleted(tutorialId))
            {
                return;
            }

            CompletedTutorials.Add(tutorialId);
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        public void ResetTutorial(string tutorialId)
        {
            if (!IsTutorialCompleted(tutorialId))
            {
                return;
            }

            CompletedTutorials.Remove(tutorialId);
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        public bool HasDifficultyAfterLoseMarker(int levelIndex)
        {
            return DifficultyAfterLoseLevelIndex.Value == levelIndex;
        }

        public void MarkDifficultyAfterLoseLevel(int levelIndex)
        {
            DifficultyAfterLoseLevelIndex.Value = levelIndex;
        }

        public void ClearDifficultyAfterLoseMarkerIfMatches(int levelIndex)
        {
            if (DifficultyAfterLoseLevelIndex.Value == levelIndex)
            {
                DifficultyAfterLoseLevelIndex.Value = -1;
            }
        }
    }
}
