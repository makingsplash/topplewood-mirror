using System;
using System.Linq;

using Newtonsoft.Json;

using SGS.Infrastructure.Tools.Common;
using SGS.Infrastructure.Tools.Configs;
using SGS.Store.Purchases.Data;

using UnityEngine;

namespace SGS.Store.Purchases
{
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class NoAdsChainConfigStepData
    {
        public string[] Items;
        public TimeSpanValue NextProductTime = new();
        public bool ShowTimers;
    }
                
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class NoAdsStoreConfigItemData
    {
        public string ItemId;
        public bool IsNoAdsPopupItem;
        public string SalePopupCardId;
    }
    
    [CreateAssetMenu(fileName = "NoAdsConfig", menuName = "Configs/NoAdsConfig")]
    [GameConfig(ConfigName = "NoAdsConfig", ConfigDir = "Configs", Group="store")]
    public class NoAdsConfig : ConfigWithJson<NoAdsConfig.NoAdsData>
    {
        [Serializable]
        [JsonObject]
        public class NoAdsData
        {
            [SerializeField]
            public TimeSpanValue RepeatDelay = new();
            
            [SerializeField]
            public TimeSpanValue RepeatDelayAdd = new();

            [SerializeField]
            public NoAdsChainConfigStepData[] Steps;

            [SerializeField]
            public int LoopStartIndex;

            [SerializeField]
            public NoAdsStoreConfigItemData[] StoreItems;

            [SerializeField]
            public string StoreBlockName;
        }

        public NoAdsData ConfigData => Config;

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Debug.Log("");
        }

        public NoAdsStoreConfigItemData GetItemById(string itemId)
        {
            return Config.StoreItems.FirstOrDefault(x =>
                x.ItemId.Equals(itemId, StringComparison.InvariantCultureIgnoreCase));
        }

        public void ParseConfig(StoreItemsConfig storeItemsConfig)
        {
            //ugly hack
            foreach (var item in Config.Steps)
            {
                if (item.ShowTimers == false)
                {
                    continue;
                }
                foreach (string id in item.Items)
                {
                    SellableItemData itemData = storeItemsConfig.AllSellableItems.FirstOrDefault(x =>
                        x.ItemId.Equals(id, StringComparison.InvariantCultureIgnoreCase));

                    if (itemData != null)
                    {
                        itemData.IsTimedOffer = true;
                        itemData.AccessTime = item.NextProductTime;
                    }
                }
            }
        }
    }
}