#if SAYKIT_PURCHASING
using System;
using Services;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Store.Purchases;
using UnityEngine;

namespace Gameplay.Sequence.StartElement
{
    [CreateAssetMenu(fileName = "GameStoreVisualConfig", menuName = "Configs/GameStoreVisualConfig")]
    public class GameStoreVisualConfig : StoreVisualConfig
    {
        public override string GetRewardText(ProductRewardData reward)
        {
            RewardType rewardType = Enum.Parse<RewardType>(reward.RewardType);
            switch (rewardType)
            {
                case RewardType.Coins:
                    return "+" + reward.Amount;
                case RewardType.InfinityHearts:
                    return GlobalService.Localization.GetLocalizedTimeInterval(TimeSpan.FromMinutes(reward.Amount));
                case RewardType.NoAds:
                    return GlobalService.Localization.GetLocalizedString("Store.Off");
                default:
                    return reward.Amount.ToString();
                
            }
        }

        public override Sprite GetSingleRewardIcon(ProductRewardData reward, string productId)
        {
            int stage = SpriteIndexFromProductId(productId); 
            return GetIcon(reward.RewardType,stage);
        }

        private int SpriteIndexFromProductId( string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return 0;
            }
            var i = productId.LastIndexOf('.');
            if (i < 0)
            {
                return 0;
            }

            if (!int.TryParse(productId.Substring(i + 1, productId.Length - i - 1), out int res))
            {
                return 0;
            }

            return res - 1;
        }
    }
}
#endif
