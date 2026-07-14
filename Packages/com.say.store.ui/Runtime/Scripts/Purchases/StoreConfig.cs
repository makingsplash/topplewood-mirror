using System.Collections.Generic;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

using SGS.Infrastructure.Tools.Common;
using SGS.Infrastructure.Tools.Configs;
using SGS.Services;
using SGS.Store.Purchases.Data;

using UnityEngine;

namespace SGS.Store.Purchases
{
    [CreateAssetMenu(fileName = "StoreConfig", menuName = "SGS/Store/StoreConfig")]
    [GameConfig(ConfigName = "StoreConfig", ConfigDir = "Configs", Group="store")]
    public class StoreConfig : ConfigWithJson<StoreConfigData>
    {
        public StoreConfigData ConfigData => Config;

        public void VerifySettings(StoreVisualConfig storeVisualConfig, PurchasesConfig purchasesConfig)
        {
            var sb = new StringBuilder();
            
            var conditionChecker = new StoreConditionChecker();

            var visualConfig = storeVisualConfig;

            HashSet<string> productNames = purchasesConfig.Purchases.Products
                .Select(product => product.ProductId)
                .ToHashSet();
            
            foreach (var block in Config.Blocks)
            {
                VerifyBlock(block, conditionChecker, sb, visualConfig);
            }
            
            if (sb.Length > 0)
            {
                GlobalService.DebugLog.LogError(sb.ToString());
            }
        }

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);

            foreach (StoreConfigPlacementData storeConfigPlacementData in Config.Placements)
            {
                var storePlacementStepData = new StorePlacementStepData
                {
                    ItemId = storeConfigPlacementData.ItemId,
                    ShowTimer = false,
                    TimeToStep = TimeSpanValue.Empty,
                };
                
                storeConfigPlacementData.Steps = new[] {storePlacementStepData};
            }
        }

        protected override List<JsonConverter> InitJsonConverters()
        {
            List<JsonConverter> converters = base.InitJsonConverters();
            converters.Add(new TimespanValueConverter());
            return converters;
        }

        private static void VerifyBlock(StoreConfigBlockData block, StoreConditionChecker conditionChecker, StringBuilder sb,
            StoreVisualConfig visualConfig)
        {
            if (!string.IsNullOrEmpty(block.ShowCondition))
            {
                conditionChecker.VerifyCondition(block.ShowCondition, sb, $"Store block {block.VisualBlockId}");
            }

            if (!visualConfig.HasBlockPrefab(block.VisualBlockId))
            {
                sb.AppendLine($"Block {block.VisualBlockId} has no visual");
            }
        }
    }
}