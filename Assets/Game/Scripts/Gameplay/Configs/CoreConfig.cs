using System;
using System.Linq;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "CoreConfig", menuName = "Configs/Core Config", order = 0)]
    public class CoreConfig : ConfigWithJson<CoreConfig.Data>
    {
        private const int DefaultAdditionalMovesOnRevive = 10;

        [Serializable]
        [JsonObject]
        public class Data
        {
            [JsonProperty]
            public int LevelReward;
            [JsonProperty]
            public int AdditionalTimeSeconds = 20;
            [JsonProperty]
            public int AdditionalMovesOnRevive = DefaultAdditionalMovesOnRevive;
            [JsonProperty]
            public int WinScreenX2ButtonStartLevel;
            [JsonProperty]
            public string RevivePricesList = "1200";


            [JsonProperty]
            public int LoseScreenOfferStartLevel = 2;
            [JsonProperty]
            public bool EnableFreeTutorialRevive;
            [JsonProperty]
            public bool EnableSingleTileColumnHelper = true;

            [NonSerialized]
            public int[] ParsedRevivePrices;
        }

        public int LevelReward => Config.LevelReward;

        public int AdditionalTimeSeconds => Config.AdditionalTimeSeconds;
        public int AdditionalMovesOnRevive => Config.AdditionalMovesOnRevive > 0
            ? Config.AdditionalMovesOnRevive
            : DefaultAdditionalMovesOnRevive;
        public int WinScreenX2ButtonStartLevel => Config.WinScreenX2ButtonStartLevel;
        public int LoseScreenOfferStartLevel => Config.LoseScreenOfferStartLevel;
        public bool EnableFreeTutorialRevive => Config.EnableFreeTutorialRevive;
        public bool EnableSingleTileColumnHelper => Config.EnableSingleTileColumnHelper;

        public int GetRevivePrice(int restartsCount)
        {
            if (Config.ParsedRevivePrices.Length == 0)
            {
                return 99;
            }

            return Config.ParsedRevivePrices[Mathf.Min(restartsCount, Config.ParsedRevivePrices.Length - 1)];
        }

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Config.ParsedRevivePrices = ParseSimpleList(Config.RevivePricesList);

        }

        private int[] ParseSimpleList(string countsString)
        {
            if ( string.IsNullOrWhiteSpace(countsString))
            {
                return null;
            }

            var splitCounts = countsString.Split(",", StringSplitOptions.RemoveEmptyEntries);
            return splitCounts.Select(int.Parse).ToArray();
        }
    }
}
