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
        // 1-based level counter, increments on every level win
        public PlayerPrefsInt CurrentLevel => SayData.Meta.CurrentLevel;
        public readonly PlayerPrefsInt CurrentLevelRestarts = new ("Meta.CurrentLevelRestarts", 0);
        public readonly PlayerPrefsInt CurrentLevelRevives = new ("Meta.CurrentLevelRevives", 0);
        public readonly PlayerPrefsInt CurrentLevelRewardedRevives = new ("Meta.CurrentLevelRewardedRevives", 0);

        // 0-based level index in levels config, calculated from CurrentLevel on level start
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
