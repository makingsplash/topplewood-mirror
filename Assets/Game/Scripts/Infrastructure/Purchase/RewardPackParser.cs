#if SAYKIT_PURCHASING
using System;
using Services;
using SGS.Infrastructure.Tools.Rewards;
using UnityEngine;
using Game.Services.LogService;

namespace Gameplay.Sequence.StartElement
{
    public static class RewardPackParser
    {

        public static RewardPack Parse(string productId, ProductRewardData[] rewards)
        {
            RewardPack pack = new() {Rewards = new Reward[rewards.Length]};

            for (int i = 0; i < rewards.Length; i++)
            {
                ProductRewardData rewardData = rewards[i];
                if (!Enum.TryParse<RewardType>(rewardData.RewardType, true, out var rewardType))
                {
                    Debug.unityLogger.Log(
                        LogType.Error,
                        $"[{LogCategory.IAP}] Unknown reward {rewardData.RewardType} in purchases config product {productId}");
                    rewardType = RewardType.Coins;
                }

                pack.Rewards[i] = new Reward
                {
                    Type = rewardType,
                    Amount = Mathf.Max(rewardData.Amount, 1),
                    Id = rewardData.Id
                };
            }

            return pack;
        }
    }
}
#endif
