using System;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens.RateUs
{ 
    [CreateAssetMenu(fileName = "RateUsConfig", menuName = "Configs/Rate Us Config", order = 0)]
    [MovedFrom(sourceNamespace: "UI.Screens.RateUs")]
    public sealed class RateUsConfig : ConfigWithJson<RateUsConfig.Data>
    {
        [Serializable]
        [JsonObject]
        [MovedFrom(sourceNamespace: "UI.Screens.RateUs")]
        public class Data
        {
            [JsonProperty] public int LevelNumberToShow = 10;
            [JsonProperty] public int MinimalPositveRate = 4;
        }
        
        public int LevelNumberToShow => Config.LevelNumberToShow;
        public int MinimalPositiveRate => Config.MinimalPositveRate;
    }
}
