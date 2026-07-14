using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Common;
using SGS.Infrastructure.Tools.Configs;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using SGS.Store.Purchases.Data;
using UnityEngine;

namespace SGS.Store.Purchases
{
    [CreateAssetMenu(fileName = "PurchasesConfig", menuName = "Configs/PurchasesConfig")]
    [GameConfig(ConfigName = "PurchasesConfig", ConfigDir = "Configs", Group="store")]
    public class PurchasesConfig : ConfigWithJson<PurchasesConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class Data
        {
            [SerializeField]
            public PurchasesData Purchases;
        }

        private PurchasesData _allPurchases;
        public PurchasesData Purchases => _allPurchases;


        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            ParseConfig();
        }

        private void ParseConfig()
        {
            _allPurchases = new PurchasesData {Products = Config.Purchases.Products.ToArray()};
        }

        public void AddProducts(ProductData[] products)
        {
            var idx = _allPurchases.Products.Length;
            Array.Resize(ref _allPurchases.Products, _allPurchases.Products.Length + products.Length);

            foreach (var productData in products)
            {
                _allPurchases.Products[idx] = productData;
                idx += 1;
            }
        }

        public void VerifySettings(StoreVisualConfig visualConfig)
        {
            var sb = new StringBuilder();
            HashSet<string> productNames = new();
            HashSet<string> storeNamesAndroid = new();
            HashSet<string> storeNamesIos = new();

            foreach (var product in Config.Purchases.Products)
            {
                if (string.IsNullOrEmpty(product.ProductId))
                {
                    sb.AppendLine($"Products list has product with empty id");
                    continue;
                }

                if (productNames.Contains(product.ProductId))
                {
                    sb.AppendLine($"Products list has duplicated product id {product.ProductId}");
                    continue;
                }

                var androidId = string.IsNullOrEmpty(product.AndroidId) ? product.ProductId : product.AndroidId;
                var iosId = string.IsNullOrEmpty(product.IosId) ? product.ProductId : product.IosId;
                if (storeNamesAndroid.Contains(androidId))
                {
                    sb.AppendLine($"Products list has duplicated Android store id {androidId}");
                    continue;
                }
                if (storeNamesIos.Contains(iosId))
                {
                    sb.AppendLine($"Products list has duplicated iOS store id {iosId}");
                    continue;
                }

                storeNamesAndroid.Add(androidId);
                storeNamesIos.Add(iosId);
                productNames.Add(product.ProductId);
            }

            foreach (var product in Config.Purchases.Products)
            {
                if (product.Rewards is null || product.Rewards.Length == 0)
                {
                    sb.AppendLine($"Product {product.ProductId} has no rewards");
                    continue;
                }

                VerifyReward(product.Rewards, product.ProductId, sb);
            }

            if (sb.Length > 0)
            {
                GlobalService.DebugLog.LogError(sb.ToString());
            }
        }

        protected override List<JsonConverter> InitJsonConverters()
        {
            List<JsonConverter> converters = base.InitJsonConverters();
            converters.Add(new TimespanValueConverter());
            return converters;
        }

        public virtual void VerifyReward(ProductRewardData[] productRewards, string productProductId, StringBuilder sb)
        {
        }
    }
}
