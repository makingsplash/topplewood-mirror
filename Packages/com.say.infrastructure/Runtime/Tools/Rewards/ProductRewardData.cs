using System;

namespace SGS.Infrastructure.Tools.Rewards
{
    [Serializable]
    public class ProductRewardData
    {
        [RewardType]
        public string RewardType;
        [RewardId]
        public string Id;
        public int Amount;
    }
}