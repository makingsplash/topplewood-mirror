using System;

using Newtonsoft.Json;

using SGS.Infrastructure.Tools.Configs;

using UnityEngine;


namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "NoAdsOfferConfig", menuName = "Configs/NoAdsOfferConfig")]
    public class NoAdsOfferConfig : ConfigWithJson<NoAdsOfferConfig.NoAdsData>
    {
        [Serializable]
        [JsonObject]
        public class NoAdsData
        {
            public bool ShowNoAdsInStart;
            public bool ShowNoAdsInGame;
            public string StoreItemNoAds;
            public string StoreItemNoAdsBundle;
            public int ProgressToShow;
        }

        public bool ShowNoAdsInStart => Config.ShowNoAdsInStart;
        public bool ShowNoAdsInGame => Config.ShowNoAdsInGame;
        public string StoreItemNoAds => Config.StoreItemNoAds;
        public string StoreItemNoAdsBundle => Config.StoreItemNoAdsBundle;
        public int ProgressToShow => Config.ProgressToShow;
    }
}
