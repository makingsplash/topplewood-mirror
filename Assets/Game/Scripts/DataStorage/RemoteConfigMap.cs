using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Configs;
using Gameplay.Configs;
using Services.AssetPreload;
using Ui.Screens.RateUs;
using UnityEngine;

#if SAYKIT_PURCHASING
    using SGS.Store.Purchases;
#endif

namespace DataStorage
{
    /// <summary>
    /// Единственный источник истины «тип конфига → поле SayKitGameConfig».
    /// Из карты читают и рантайм (Configs.InitWithRemote), и инспектор
    /// ConfigWithJson-ассетов (ConfigRemoteFieldHeader) — рассинхрон невозможен.
    /// </summary>
    public static class RemoteConfigMap
    {
        private static readonly IReadOnlyDictionary<Type, string> ConfigTypeToRemoteField = new Dictionary<Type, string>
        {
            [typeof(CoreConfig)] = nameof(SayKitGameConfig.config_core),
            [typeof(StartConfig)] = nameof(SayKitGameConfig.config_start),
            [typeof(RewardedConfig)] = nameof(SayKitGameConfig.config_rewarded),
            [typeof(RateUsConfig)] = nameof(SayKitGameConfig.config_rate_us),
            [typeof(InputConfig)] = nameof(SayKitGameConfig.config_input),
            [typeof(NoAdsOfferConfig)] = nameof(SayKitGameConfig.config_no_ads),
            [typeof(HeartConfig)] = nameof(SayKitGameConfig.config_heart),
            [typeof(AssetPreloadConfig)] = nameof(SayKitGameConfig.config_asset_preload),
            [typeof(BoostersConfig)] = nameof(SayKitGameConfig.config_boosters), // TASK-237: pre-existing unused field
            [typeof(AdsConfig)] = nameof(SayKitGameConfig.config_ads),           // TASK-658: between-levels interstitial

#if SAYKIT_PURCHASING
            [typeof(PurchasesConfig)] = nameof(SayKitGameConfig.config_purchases),
            [typeof(StoreConfig)] = nameof(SayKitGameConfig.config_store),
            [typeof(StoreItemsConfig)] = nameof(SayKitGameConfig.config_store_items),
#endif
        };

        public static bool TryGetRemoteFieldName(Type configType, out string fieldName)
        {
            return ConfigTypeToRemoteField.TryGetValue(configType, out fieldName);
        }

        public static string GetRemoteJson(SayKitGameConfig remoteConfig, Type configType)
        {
            if (!TryGetRemoteFieldName(configType, out var fieldName))
            {
                Debug.LogError($"Error: config {configType.Name} is not registered in {nameof(RemoteConfigMap)}");
                return null;
            }

            var field = typeof(SayKitGameConfig).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            return (string)field.GetValue(remoteConfig);
        }
    }
}
