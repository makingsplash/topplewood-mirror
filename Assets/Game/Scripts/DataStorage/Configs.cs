using DynamicDebugMenu;
using Services.AssetPreload;
using SGS.Configs;
using SGS.Data;
using SGS.Infrastructure.Tools.Configs;
using Ui.Components.Message;
using Ui.Screens.RateUs;
using Game.Services.LogService;
using Game.Configs;
using Gameplay.Configs;

#if SAYKIT_PURCHASING
    using SGS.Store.Purchases;
#endif


namespace DataStorage
{
    public class Configs
    {
        public AnimationsConfig Animations;
        public AudioConfig Audio => SayData.Configs.Audio;
        public DebugItemsConfig DebugItems => SayData.Configs.DebugItems;
        public UiPrefabs Ui => SayData.Configs.Ui;
        public CoreConfig Core;
        public LogServiceConfig LogServiceConfig;
        public StartConfig Start;
        public RewardedConfig RewardedConfig;
        public MessagesViewConfig MessagesConfig;
        public RateUsConfig RateUsConfig;
        public HeartConfig HeartConfig;
        public BoostersConfig BoostersConfig;
        public AdsConfig AdsConfig;

        public InputConfig InputConfig;
        public NoAdsOfferConfig NoAdsOfferConfig;
        public AssetPreloadConfig AssetPreloadConfig;


#if SAYKIT_PURCHASING
        public PurchasesConfig Purchase;
        public StoreConfig StoreConfig;
        public StoreItemsConfig StoreItemsConfig;
        public StoreVisualConfig StoreVisual;
#endif

        public void InitWithRemote(SayKitGameConfig remoteConfig)
        {
            Init(Core, remoteConfig);
            Init(Start, remoteConfig);
            Init(RewardedConfig, remoteConfig);
            Init(RateUsConfig, remoteConfig);
            Init(InputConfig, remoteConfig);
            Init(NoAdsOfferConfig, remoteConfig);
            Init(HeartConfig, remoteConfig);
            // TASK-237: the BoostersConfig ScriptableObject is assigned in the editor; guard the null so a build
            // without it assigned still boots (the booster then runs on its C# defaults — see HintBoosterService).
            if (BoostersConfig != null)
                Init(BoostersConfig, remoteConfig);
            // TASK-658: same guard — without the asset the interstitial runs on AdsConfig's C# defaults.
            if (AdsConfig != null)
                Init(AdsConfig, remoteConfig);
            Init(AssetPreloadConfig, remoteConfig);

#if SAYKIT_PURCHASING
            Init(Purchase, remoteConfig);
            Init(StoreConfig, remoteConfig);
            Init(StoreItemsConfig, remoteConfig);
#endif
        }

        private static void Init<TData>(ConfigWithJson<TData> config, SayKitGameConfig remoteConfig)
        {
            config.InitWithRemote(RemoteConfigMap.GetRemoteJson(remoteConfig, config.GetType()));
        }
    }
}
