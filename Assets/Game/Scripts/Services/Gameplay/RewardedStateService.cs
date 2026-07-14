using System.Linq;

using DataStorage;
using SGS.Services;
using SGS.Services.Interfaces;
#if SAYKIT_PURCHASING
using SGS.Store.Purchases;
#endif

using UnityEngine;


namespace Services.Gameplay
{
    public class RewardedStateService : IService
    {
        public void Init()
        {
            InitStartLevels();

#if SAYKIT_PURCHASING
            GlobalService.PurchaseService.OnPurchaseCompleteEvent += ProcessPayment;
#endif
            Data.Meta.CurrentLevel.OnChanged += LevelChanged;
            Data.Meta.CurrentLevelRestarts.OnChanged += LevelRestartsChanged;
            Data.Meta.CurrentLevelRewardedRevives.OnChanged += RewardedRevivesChanged;
            
            UpdateState();
        }

        public void DeInit()
        {
#if SAYKIT_PURCHASING
            GlobalService.PurchaseService.OnPurchaseCompleteEvent -= ProcessPayment;
#endif
            Data.Meta.CurrentLevel.OnChanged -= LevelChanged;
            Data.Meta.CurrentLevelRestarts.OnChanged -= LevelRestartsChanged;
            Data.Meta.CurrentLevelRewardedRevives.OnChanged -= RewardedRevivesChanged;
        }

        public void DebugResetState()
        {
#if GPIE_DEVELOPMENT
            var state = Data.Meta.RewardedState;
            state.InterstitialWasShown.Value = false;
            state.IntersEnableLevel.Value = -1;
            state.AdBoostersWasShown.Value = false;
            state.AdBoostersEnableLevel.Value = -1;

            InitStartLevels();
            UpdateState();
#endif            
        }


        public int DebugGetPurchasesCount()
        {
           return  GetPurchasesCount();
        }
        
        private void InitStartLevels()
        {
            var rewardedConfig = Data.Configs.RewardedConfig;
            var state = Data.Meta.RewardedState;
            if (state.IntersEnableLevel.Value < 0)
            {
                if (!state.InterstitialWasShown.Value)
                {
                    state.IntersEnableLevel.Value = rewardedConfig.Inters.StartAfterLevel +
                        rewardedConfig.Inters.PurchaseDelayBeforeStart * GetPurchasesCount();
                }
                else
                {
                    state.IntersEnableLevel.Value = 0;
                }
            }

            if (state.AdBoostersEnableLevel.Value < 0)
            {
                state.AdBoostersEnableLevel.Value = rewardedConfig.AdBoosters.StartAfterLevel +
                    rewardedConfig.AdBoosters.PurchaseDelayBeforeStart * GetPurchasesCount();
            }
            
            if (state.AdHeartsEnableLevel.Value < 0)
            {
                state.AdHeartsEnableLevel.Value = rewardedConfig.AdHearts.StartAfterLevel +
                    rewardedConfig.AdHearts.PurchaseDelayBeforeStart * GetPurchasesCount();
            }
        }

        private static int GetPurchasesCount()
        {
            int numPurchases = 0;
            
#if SAYKIT_PURCHASING
            numPurchases = Data.Purchases.PurchaseHistory.Values.Sum(d => d.TotalCount - d.RefundsCount);
            if (GlobalService.SayKit.IsPremium())
            {
                numPurchases = Mathf.Max(numPurchases, 1);
            }
#endif

            return numPurchases;
        }

        private void LevelChanged(int _)
        {
            UpdateState();
        }
        
        private void LevelRestartsChanged(int _)
        {
            UpdateState();
        }

        private void RewardedRevivesChanged(int _)
        {
            UpdateState();
        }
        
#if SAYKIT_PURCHASING
        private void ProcessPayment(string _, int quantity, PurchaseService.PurchaseCompleteStatus status)
        {
            var rewardedConfig = Data.Configs.RewardedConfig;
            var state = Data.Meta.RewardedState;
            if (!state.InterstitialWasShown.Value)
            {
                state.IntersEnableLevel.Value += rewardedConfig.Inters.PurchaseDelayBeforeStart;
            }
            else
            {
                state.IntersEnableLevel.Value =
                    Data.Meta.CurrentLevel.Value + rewardedConfig.Inters.PurchaseDelayAfterStart;
                state.InterstitialsEnabled.Value =
                    Data.Meta.CurrentLevel.Value > state.IntersEnableLevel.Value;
            }
            
            if (!state.AdBoostersWasShown.Value)
            {
                state.AdBoostersEnableLevel.Value += rewardedConfig.AdBoosters.PurchaseDelayBeforeStart;
            }
            else
            {
                if (state.AdBoostersEnabled.Value)
                {  
                    state.AdBoostersDisableLevel.Value = Data.Meta.CurrentLevel.Value; 
                }

                state.AdBoostersEnableLevel.Value = Data.Meta.CurrentLevel.Value + rewardedConfig.AdBoosters.PurchaseDelayAfterStart;
            }
            if (!state.AdHeartsWasShown.Value)
            {
                state.AdHeartsEnableLevel.Value += rewardedConfig.AdHearts.PurchaseDelayBeforeStart;
            }
            else
            {
                if (state.AdHeartsEnabled.Value)
                {  
                    state.AdHeartsDisableLevel.Value = Data.Meta.CurrentLevel.Value; 
                }

                state.AdHeartsEnableLevel.Value = Data.Meta.CurrentLevel.Value + rewardedConfig.AdHearts.PurchaseDelayAfterStart;
            }

            UpdateState();
        }
#endif


        private void UpdateState()
        {
            var rewardedConfig = Data.Configs.RewardedConfig;
            var state = Data.Meta.RewardedState;
            
            // state.NoAdOffersEnabled.Value = state.InterstitialWasShown.Value;
            state.InterstitialsEnabled.Value = Data.Meta.CurrentLevel.Value >= state.IntersEnableLevel.Value;

            state.AdBoostersEnabled.Value =
                Data.Meta.CurrentLevel.Value >= state.AdBoostersEnableLevel.Value ||
                Data.Meta.CurrentLevel.Value <= state.AdBoostersDisableLevel.Value;

            if (state.AdBoostersEnabled.Value)
            {
                state.AdBoostersWasShown.Value = true;
            }
            
            state.AdHeartsEnabled.Value =
                Data.Meta.CurrentLevel.Value >= state.AdHeartsEnableLevel.Value ||
                Data.Meta.CurrentLevel.Value <= state.AdHeartsDisableLevel.Value;

            if (state.AdHeartsEnabled.Value)
            {
                state.AdHeartsWasShown.Value = true;
            }

            if (Data.Meta.CurrentLevel.Value < rewardedConfig.Revives.StartAfterLevel)
            {
                state.ReviveForRewardEnabled.Value = false;
            }
            else
            {
                var restartsValues = GetPurchasesCount() > 0
                    ? rewardedConfig.Revives.ParsedDelaysForPayers
                    : rewardedConfig.Revives.ParsedDelaysForNonPayers;
                var restartsCount = restartsValues[Data.Meta.CurrentLevel.Value];
                var withinRestartsThreshold = Data.Meta.CurrentLevelRestarts.Value >= restartsCount;

                var maxRevivesForRewarded = rewardedConfig.Revives.MaxRevivesForRewarded;
                var withinReviveLimit = maxRevivesForRewarded < 0
                    || Data.Meta.CurrentLevelRewardedRevives.Value < maxRevivesForRewarded;

                state.ReviveForRewardEnabled.Value = withinRestartsThreshold && withinReviveLimit;
            }

        }

    }
}
