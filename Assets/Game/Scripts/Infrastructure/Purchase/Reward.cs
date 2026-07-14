#if SAYKIT_PURCHASING
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Gameplay.Sequence.StartElement
{
    public enum RewardType
    {
        Coins = 1,
        NoAds = 2,
        Hearts = 8,
        InfinityHearts = 9,
    }

    [Serializable]
    public class Reward
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RewardType Type;
        public int Amount;
        public string Id;

        public bool HasAmount()
        {
            switch (Type)
            {
                case RewardType.Coins:
                case RewardType.Hearts:
                case RewardType.InfinityHearts:
                    return true;
            }
            return false;
        }
        
        public bool HasId()
        {
            return false;
        }
    }
}
#endif
