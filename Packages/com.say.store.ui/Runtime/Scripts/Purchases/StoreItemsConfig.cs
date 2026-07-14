using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SGS.Infrastructure.Tools.Configs;
using SGS.Services;
using SGS.Store.Purchases.Data;

using UnityEngine;

namespace SGS.Store.Purchases
{
    [CreateAssetMenu(fileName = "StoreItemsConfig", menuName = "SGS/Store/StoreItemsConfig")]
    [GameConfig(ConfigName = "StoreItemsConfig", ConfigDir = "Configs", Group="store")]
    public class StoreItemsConfig : ConfigWithJson<SellableItemsConfigData>
    {
        public SellableItemData[] AllSellableItems => Config.AllSellableItems;
        
        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            
            Config.AllSellableItems = Config.StoreItems.Concat(Config.NonStoreItems).ToArray();
        }
        
        public void VerifySettings(StoreVisualConfig storeVisualConfig, PurchasesConfig purchasesConfig)
        {
            var sb = new StringBuilder();
            
            var conditionChecker = new StoreConditionChecker();

            HashSet<string> productNames = purchasesConfig.Purchases.Products
                .Select(product => product.ProductId)
                .ToHashSet();
            
            foreach (var itemData in Config.AllSellableItems)
            {
                if (!productNames.Contains(itemData.ProductId))
                {
                    sb.AppendLine($"Item id {itemData.ProductId} is not in products list");
                }
                
                VerifyItem(itemData, conditionChecker, sb, storeVisualConfig);
            }
            
            if (sb.Length > 0)
            {
                GlobalService.DebugLog.LogError(sb.ToString());
            }
        }
        
        private static void VerifyItem(SellableItemData itemData, StoreConditionChecker conditionChecker, StringBuilder sb,
            StoreVisualConfig visualConfig)
        {
            if (!string.IsNullOrEmpty(itemData.ShowCondition))
            {
                conditionChecker.VerifyCondition(itemData.ShowCondition, sb, $"Store item {itemData.ItemId}");
            }

            if (!visualConfig.HasCardPrefab(itemData.VisualSettings.VisualId))
            {
                sb.AppendLine($"Item {itemData.ItemId} has unknown visual {itemData.VisualSettings.VisualId}");
            }

            if (itemData.LimitedAvailabilitySettings.HasCooldown && itemData.LimitedAvailabilitySettings.CooldownTime <= TimeSpan.Zero)
            {
                sb.AppendLine($"Item {itemData.ItemId} has cooldown but no cooldown time");
            }

            if (itemData.IsTimedOffer && itemData.AccessTime <= TimeSpan.Zero)
            {
                sb.AppendLine($"Item {itemData.ItemId} is timed but no acces time");
            }
        }
    }
}