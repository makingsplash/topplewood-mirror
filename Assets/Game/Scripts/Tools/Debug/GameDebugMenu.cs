using System;
using DataStorage;
using DynamicDebugMenu.Elements;
using Services;
using Services.DebugTools.Submenu;
using SGS.Data;
using SGS.Extensions;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using SGS.Services.DebugTools;
using UnityEngine;
using UnityEngine.UI;
using Game.Services.LogService;

namespace Tools.Debug
{
    public static class GameDebugMenu
    {
        private static int _level;

        public static void Create(DDMContext context)
        {
            _level = SayData.Meta.CurrentLevel.Value;

            CreateApplicationFlow(context);
            CreateGoToLevel(context);
            CreateResources(context);

            DDMInputButton.Create(context, "Input Drag Threshold", "Set", (int)Data.Configs.InputConfig.DragDetectionDistance,
                amount =>
                {
                    Data.Configs.InputConfig.DragDetectionDistance = amount;
                });


            DDMButton.Create(context, "Clear seen messages", () =>
            {
                Data.Messages.ClearSeenMessages();
            });

            DDMButton.Create(context, "Toggle UI", () =>
            {
                Data.Scene.UiCamera.gameObject.SetActive(!Data.Scene.UiCamera.gameObject.activeSelf);
            });

            LogCategoryFilterSubmenu.Create(context);
            PremiumAndAdsSubmenu.Create(context);
            RewardedStateSubmenu.Create(context);
            PurchasesSubmenu.Create(context);
            PopupsSubmenu.Create(context);
            LanguageSubmenu.Create(context);

            DefaultDebugMenu.CreateShortMenuFooter(context);
        }


        private static void CreateApplicationFlow(DDMContext context)
        {
            DDMButton.Create(context, "Reset Progress", () =>
            {
                // TASK-044: no direct UnityEngine.PlayerPrefs — go through the Say wrapper. DeleteAllPrefs wipes
                // every key the backend has touched this session; the campaign reset is GUARANTEED by explicitly
                // setting CurrentLevel=1 (the campaign reads only CurrentLevel) and keeping the 0-based mirror
                // coherent, then flushing durably.
                PlayerPrefsCollection.Instance.DeleteAllPrefs();
                SayData.Meta.CurrentLevel.Value = 1;
                SayData.Meta.CurrentLevelIndex.Value = 0;
                PlayerPrefsCollection.Instance.SaveDataIfChanged();

                context.Main.SetRestart();
                context.Main.CloseMenu();
            });
        }

        private static void CreateGoToLevel(DDMContext context)
        {
            DDMInputButton.Create(context, "Go to Level: ", "Ok", InputField.ContentType.IntegerNumber,
                () => _level.ToString(),
                stringValue =>
                {
                    if (int.TryParse(stringValue, out int result))
                    {
                        _level = result;
                    }

                    SayData.Meta.CurrentLevel.Value = _level;
                    Data.Meta.ClearProgress(_level - 1);

                    context.Main.SetRestart();
                    context.Main.CloseMenu();
                });

            DDMLabel.Create(context, $"Level restarts: {Data.Meta.CurrentLevelRestarts.Value}, revives: {Data.Meta.CurrentLevelRevives.Value}");
        }

        private static void CreateResources(DDMContext context)
        {
            DDMLabel.Create(context, "Resources");

            DDMInputButton.Create(context, "Gold", "Set", Data.Meta.GoldAmount.Value,
                amount =>
                {
                    Data.Meta.GoldAmount.Value = amount;
                });
            DDMInputButton.Create(context, "Hearts", "Set", Data.Meta.HeartsAmount.Value,
                amount =>
                {
                    Data.Meta.HeartsAmount.Value = amount;
                });
            DDMInputButton.Create(context, "InfinityHeartTime", "Add", InputField.ContentType.Standard, GlobalService.Hearts.InfinityHeartsTimeLeft().ToConfigString(),
                value =>
                {
                    GlobalService.Hearts.AddInfinityHeartsDuration(TimeSpanExtensions.FromConfigString(value));
                });
        }
    }

    public static class LogCategoryFilterSubmenu
    {
        public static void Create(DDMContext context)
        {
            ApplyStoredState();
            DDMSubmenu.Create(context, "Console Filter", () => context.Main.CreatePage("Console Filter", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
#if GPIE_DEVELOPMENT
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                if (category == LogCategory.None)
                    continue;

                var localCategory = category;
                DDMCheckbox.Create(
                    context,
                    localCategory.ToString(),
                    () => WrapperUnityLogService.IsCategoryEnabled(localCategory),
                    enabled => SetCategoryEnabled(localCategory, enabled));
            }
#endif
        }

        private static void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            WrapperUnityLogService.SetCategoryEnabled(category, enabled);
            SaveCurrentMask();
        }

        private static void ApplyStoredState()
        {
            var mask = Data.Debug.LogCategoryMask.Value;
            if (mask < 0)
                return;

            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                if (category == LogCategory.None)
                    continue;

                var isEnabled = IsBitSet(mask, category);
                WrapperUnityLogService.SetCategoryEnabled(category, isEnabled);
            }
        }

        private static void SaveCurrentMask()
        {
            int mask = 0;
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                if (category == LogCategory.None)
                    continue;

                if (WrapperUnityLogService.IsCategoryEnabled(category))
                    mask |= 1 << (int)category;
            }

            Data.Debug.LogCategoryMask.Value = mask;
        }

        private static bool IsBitSet(int mask, LogCategory category) =>
            (mask & (1 << (int)category)) != 0;
    }
}
