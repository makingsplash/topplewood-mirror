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
            // TASK-612: StartGoldAmount removed with the coin currency (a stale remote value is ignored by Newtonsoft).
            [JsonProperty]
            public int HomeScreenAvailableLevel = 4;
        }

        public int HomeScreenAvailableLevel => Config.HomeScreenAvailableLevel;
    }
}
