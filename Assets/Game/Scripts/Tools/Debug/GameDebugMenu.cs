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

#if GPIE_DEVELOPMENT
            // TASK-562: the level line heads the menu (it left the HUD). Dynamic: DDMLabel re-reads it every frame, so it
            // follows ◀ Prev / Next ▶ / Jump to pool, which change the level without closing the menu.
            DDMLabel.CreateDynamic(context, PositionLabel);
#endif

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

                // TASK-162: DeleteAllPrefs only wipes the backend — it does NOT refresh the in-memory RealtimeValue
                // caches, so Gold/Hearts/HintCharges keep returning the stale value and the next SaveDataIfChanged
                // writes it straight back (the counter never even fires OnChanged). RefreshAll re-reads every live
                // pref from the wiped backend → each returns its default, and OnChanged updates the HUD counters.
                // With live gold (TASK-240) this is what makes a dev reset honest.
                PlayerPrefsCollection.Instance.RefreshAll();

                SayData.Meta.CurrentLevel.Value = 1;
                SayData.Meta.CurrentLevelIndex.Value = 0;
                // TASK-528: forget "line-up cleared" and the pool place EXPLICITLY — DeleteAllPrefs only wipes keys
                // touched this session, which would make the reset correct by coincidence, not by construction.
                Data.Meta.LineupCleared.Value = 0;
                Data.Meta.PoolCursor.Value = 0;

                // Re-seed the start bank so the reset lands on a true fresh-install state WITHOUT relaunching (Reset
                // is a reload-in-place, not a reboot — architect §7). The guard was just cleared by RefreshAll, so
                // this fires; on a real fresh install the boot-time EconomySeedService already did it. (Hint charges
                // re-seed on the next process launch via HintBoosterService's ctor — a dev-only same-session gap,
                // noted in the ticket.)
                Services.Gameplay.EconomySeedService.SeedIfNeeded();

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

                    // TASK-528: "Go to Level N" means line-up level N (first pass), pool keys dropped — but Ok on the
                    // prefilled CURRENT number reloads in place, so it no longer throws a pool player onto the final
                    // line-up level (QA Н-1). The rule lives in CampaignCursor.GoToLevel (tested in EditMode).
                    var current = new Gameplay.Core.CampaignProgress(
                        SayData.Meta.CurrentLevel.Value, Data.Meta.LineupCleared.Value, Data.Meta.PoolCursor.Value);
                    var target = Gameplay.Core.CampaignCursor.GoToLevel(current, _level);
                    // TASK-537 (+ debt TASK-175): the SAME write as the ◀/▶ arrows (DevStepPersisted → store.Write) —
                    // all keys coherently, the 0-based CurrentLevelIndex mirror included, then a durable flush. The
                    // old direct Value writes were only staged SetInt, so a kill in focus dropped the jump.
                    new Gameplay.Adapters.MetaCampaignStore().Write(target);
                    Data.Meta.ClearProgress(_level - 1);

                    context.Main.SetRestart();
                    context.Main.CloseMenu();
                });

#if GPIE_DEVELOPMENT
            // TASK-174: page the line-up ◀/▶ — the jump happens IMMEDIATELY and the menu STAYS OPEN (unlike the
            // number input above, which closes it). GameDebugMenuLevelStep is a dev-only type, so both the calls and
            // the StepLevel helper are guarded; the reference is fully qualified because in a player build the
            // Gameplay.Dev namespace is empty and a `using` would not compile.
            DDMButton.Create(context, "◀ Prev", () => StepLevel(-1));
            DDMButton.Create(context, "Next ▶", () => StepLevel(1));
            // TASK-528: enter the pool circle at place 1 (persisted; ◀/▶ then go around the circle).
            DDMButton.Create(context, "Jump to pool", JumpToPool);
#endif

            DDMLabel.Create(context, $"Level restarts: {Data.Meta.CurrentLevelRestarts.Value}, revives: {Data.Meta.CurrentLevelRevives.Value}");
        }

#if GPIE_DEVELOPMENT
        private static string PositionLabel() =>
            Gameplay.Dev.GameDebugMenuLevelStep.PositionLabel?.Invoke() ?? "L —: no gameplay scope";

        private static void StepLevel(int delta)
        {
            Gameplay.Dev.GameDebugMenuLevelStep.Request(delta);   // in-scope: step + persist + reload, SYNCHRONOUS
            _level = SayData.Meta.CurrentLevel.Value;             // sync app bookkeeping to the clamped value
            Data.Meta.ClearProgress(_level - 1);                  // parity with "Go to Level" (no-op for ShapeFill)
            // NO CloseMenu() — the menu stays open (the core of the task).
        }

        private static void JumpToPool()
        {
            Gameplay.Dev.GameDebugMenuLevelStep.RequestPoolJump();   // in-scope: persist + reload, SYNCHRONOUS
            _level = SayData.Meta.CurrentLevel.Value;               // sync app bookkeeping to the pool's number
        }
#endif

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
