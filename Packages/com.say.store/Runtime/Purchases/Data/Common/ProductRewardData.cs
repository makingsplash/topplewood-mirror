#if !SAY_INFRASTURCTURE

using System;

namespace Tools.Purchases.Data.Common
{
    public class RewardTypeAttribute : Attribute {}
    public class RewardIdAttribute : Attribute {}
    
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
#endif