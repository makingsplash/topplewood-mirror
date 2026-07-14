using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

using SGS.Infrastructure.Tools.Configs;
using SGS.Services;

using UnityEngine;

namespace SGS.Store.Purchases
{
    [CreateAssetMenu(fileName = "OffersConfig", menuName = "SGS/Store/OffersConfig")]
    [GameConfig(ConfigName = "OffersConfig", ConfigDir = "Configs", Group="store")]
    public class OffersConfig : ConfigWithJson<OffersConfig.Data>
    {
        public Offer[] Offers => Config.Offers;
        
        [Serializable]
        [JsonObject]
        public class Data
        {
            public Offer[] Offers;
        }
        
        [Serializable]
        [JsonObject]
        public class Offer
        {
            [SerializeField]
            public string Placement;
            
            [SerializeField]
            public OffersConfigItem[] ItemsList;

            [SerializeField]
            public int MaxPurchases;
            
            [SerializeField]
            public int NextCooldown = 1;
        }
        
        [Serializable]
        [JsonObject]
        public class OffersConfigItem
        {
            [SerializeField]
            public string ItemId;
            [SerializeField]
            public string ItemIfBought;
            [SerializeField]
            public string ItemIfNotBought;
            [SerializeField]
            public int SessionsToWaitForChange;
        }


        public void VerifySettings(PurchasesConfig purchasesConfig)
        {
            var sb = new StringBuilder();
            
            HashSet<string> productNames = purchasesConfig.Purchases.Products
                .Select(product => product.ProductId)
                .ToHashSet();
            
            foreach (var offer in Config.Offers)
            {
                VerifyOffer(offer, productNames, sb);
            }
            
            if (sb.Length > 0)
            {
                GlobalService.DebugLog.LogError(sb.ToString());
            }
        }

        private static void VerifyOffer(Offer offer, HashSet<string> productNames, StringBuilder resultString)
        {
            if (string.IsNullOrEmpty(offer.Placement))
            {
                resultString.AppendLine($"Offers list has offer with empty placement");
            }
                
            foreach (var product in offer.ItemsList)
            {
                if (!productNames.Contains(product.ItemId))
                {
                    resultString.AppendLine($"Product {product.ItemId} in offer {offer.Placement} is not in products list");
                }
                if (!string.IsNullOrEmpty(product.ItemIfBought)
                    && !productNames.Contains(product.ItemIfBought))
                {
                    resultString.AppendLine($"Product {product.ItemIfBought} in offer {offer.Placement}is not in products list");
                }
                if (!string.IsNullOrEmpty(product.ItemIfNotBought)
                    && !productNames.Contains(product.ItemIfNotBought))
                {
                    resultString.AppendLine($"Product {product.ItemIfNotBought} in offer {offer.Placement}is not in products list");
                }
                if (!string.IsNullOrEmpty(product.ItemIfBought)
                    && Array.FindIndex(offer.ItemsList, configProduct => configProduct.ItemId == product.ItemIfBought) == -1)
                {
                    resultString.AppendLine($"In offer {offer.Placement} can't find settings for {product.ItemIfBought}");
                }
                if (!string.IsNullOrEmpty(product.ItemIfNotBought)
                    && Array.FindIndex(offer.ItemsList, configProduct => configProduct.ItemId == product.ItemIfNotBought) == -1)
                {
                    resultString.AppendLine($"In offer {offer.Placement} can't find settings for {product.ItemIfNotBought}");
                }
            }
        }
    }
}