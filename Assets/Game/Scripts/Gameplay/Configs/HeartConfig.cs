using System;
using UnityEngine;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;

namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "HeartConfig", menuName = "Configs/Heart Config", order = 0)]
    public class HeartConfig : ConfigWithJson<HeartConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class Data
        {
            [JsonProperty] public bool EnableHearts;
            [JsonProperty] public int StartHeartsAmount;
            [JsonProperty] public int MaxHeartsCount = 5;
            [JsonProperty] public int RewardHeartsCount;
            [JsonProperty] public int HeartCost = 900;

            [JsonProperty] public int RechargeHeartTimeMinutes = 30;
        }

        public bool EnableHearts => Config.EnableHearts;
        public int StartHeartsAmount => Config.StartHeartsAmount;
        public int MaxHeartsCount => Config.MaxHeartsCount;
        public int RewardHeartsCount => Config.RewardHeartsCount;
        public int RechargeHeartTimeMinutes => Config.RechargeHeartTimeMinutes;
        public int HeartCost => Config.HeartCost;

    }
}
