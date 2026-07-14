using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;

namespace Services.AssetPreload
{
    [GameConfig(ConfigName = "AssetPreloadConfig", ConfigDir = "Game/Configs")]
    public class AssetPreloadConfig : ConfigWithJson<AssetPreloadConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class Data
        {
            [JsonProperty("BackgroundDownloading")] public bool _backgroundDownloading = true;
            [JsonProperty("EnableCDNUpdate")] public bool _enableCDNUpdate = true;
        }

        public bool BackgroundDownloading => Config._backgroundDownloading;
        public bool EnableCDNUpdate => Config._enableCDNUpdate;
    }
}

