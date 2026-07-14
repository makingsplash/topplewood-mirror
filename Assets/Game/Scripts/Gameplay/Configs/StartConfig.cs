using System;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "StartConfig", menuName = "Configs/Start Config", order = 0)]
    public class StartConfig : ConfigWithJson<StartConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class Data
        {
            [JsonProperty]
            public int StartGoldAmount;
            [JsonProperty]
            public int HomeScreenAvailableLevel = 4;    
        }

        public int StartGoldAmount => Config.StartGoldAmount;
        public int HomeScreenAvailableLevel => Config.HomeScreenAvailableLevel;
    }
}
