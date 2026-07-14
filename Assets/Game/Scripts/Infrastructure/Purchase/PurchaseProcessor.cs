using UnityEngine;
#if SAYKIT_PURCHASING
using System;
using DataStorage;
using Services;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Store.Purchases;
using Tools.Constants;

namespace Gameplay.Sequence.StartElement
{
    public class PurchaseProcessor : IPurchaseProcessor
    {

        public ProductRewardData[] GetActualReward(string productId, ProductRewardData[] pack)
        {
            return pack;
        }

        public void ProcessPurchase(string productId, ProductRewardData[] pack, int quantity, bool restore, Action onComplete)
        {
            var rewardPack = RewardPackParser.Parse(productId, pack);
            foreach (var reward in rewardPack.Rewards)
            {
                if (restore)
                {
                    RestoreReward(reward.Type);
                }
                else
                {
                    GiveReward(reward.Type, reward.Amount * quantity, reward.Id);
                }
            }
            onComplete?.Invoke();
        }

        public void ProcessRefund(string productId, Action onComplete)
        {
            if (Data.Purchases.PurchasedNonConsumables.Count == 0)
            {
                GlobalService.SayKit.DisablePremium();
            }

            onComplete?.Invoke();
        }


        private void RestoreReward(RewardType rewardType)
        {
            switch (rewardType)
            {
                case RewardType.NoAds:
                    GlobalService.SayKit.EnablePremium();
                    break;
            }
        }

        private void GiveReward(RewardType rewardType, int rewardAmount, string rewardId)
        {
            switch (rewardType)
            {
                case RewardType.Coins:
                    var goldBeforeIncome = Data.Meta.GoldAmount.Value;
                    Data.Meta.GoldAmount.Value += rewardAmount;
                    //Data.Meta.VisualGoldAmount.Value += rewardAmount;
                    GlobalService.Analytics.Resource.SendSoftIncome(rewardAmount, goldBeforeIncome, Placement.PurchaseIncome);
                    break;
                case RewardType.NoAds:
                    GlobalService.SayKit.EnablePremium();
                    break;
                case RewardType.Hearts:
                    GlobalService.Hearts.AddHearts(rewardAmount);
                    break;
                case RewardType.InfinityHearts:
                    GlobalService.Hearts.AddInfinityHeartsDuration(TimeSpan.FromMinutes(rewardAmount));
                    break;
            }
        }
    }
}
#else
[CreateAssetMenu(fileName = "GameStoreVisualConfig", menuName = "Configs/GameStoreVisualConfig")]
public class GameStoreVisualConfig : ScriptableObject
{
    // fallback без зависимостей, чтобы сериализация не падала
}
#endif
