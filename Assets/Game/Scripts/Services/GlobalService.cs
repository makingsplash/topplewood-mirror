using System;
using DataStorage;
using Services.AssetPreload;
using SGS.Services.Audio;
using SGS.Services.Containers;
using SGS.Services.Date_Time;
using SGS.Services.DebugLog;
using SGS.Services.DebugTools;
using SGS.Services.Haptic;
using SGS.Services.Localization;
using SGS.Services.SayKit;
using Game.Services.AssetManagement;
using UnityEngine;

#if SAYKIT_PURCHASING
    using SGS.Store.Purchases;
#endif

namespace Services
{
    public class GlobalService 
    {
        public static void Create()
        {
            GlobalServiceContainer.Create();
            CreateServices();
        }
        
        private static void CreateServices()
        {
            GlobalServiceContainer.Register(new DebugLogService(new DebugLogLogic(DebugLogType.FromIntMask(Data.Debug.DebugLogType.Value))));
            // TASK-533: the smoke player has no SayKit Java bridge (SayKitSmokeBuildFixup strips it, SayKitInit skips
            // init), and ANY bridge call there never returns: it fails with NoClassDefFoundError, SKThreadService.HandleError
            // reports the failure through the same dead bridge (TrackEvent), which fails again — an endless error loop on
            // the main thread (measured on device: boot frozen inside InitializeGlobalServices, ~500 errors/s). The template's
            // own no-SayKit seam is used there instead. Prod player (no define) and the editor keep the real service.
            ISayKitService sayKitService =
#if SAYKIT && !(SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR)
                new SayKitService();
#else
                new EmptySayKitService();
#endif
            GlobalServiceContainer.Register<ISayKitService>(sayKitService);
            GlobalServiceContainer.Register(new GameAnalyticsService());
            GlobalServiceContainer.Register(new TimerService());
            GlobalServiceContainer.Register(new GameTimerService());
            GlobalServiceContainer.Register(new AssetPreloadService());
            GlobalServiceContainer.Register(new CatalogUpdateService());
            GlobalServiceContainer.Register(new AssetStatusChecker());
            GlobalServiceContainer.Register(AudioService.Create());
            GlobalServiceContainer.Register(new HapticService());
            GlobalServiceContainer.Register(new DateService());
            GlobalServiceContainer.Register(new HeartsService());

#if SAYKIT_PURCHASING
            try
            {
                GlobalServiceContainer.Register(new GamePurchaseService(new PlayerPrefsPurchasesPersistentStorage(),
                    Data.Configs.Purchase, Data.Configs.StoreConfig, Data.Configs.StoreItemsConfig,
                    Data.Configs.StoreVisual));
                // GlobalServiceContainer.Register(new OffersService());
            }
            catch (Exception ex)
            {
                DebugLog.LogError($"IAP init failed; purchases disabled. {ex}");
#if !UNITY_EDITOR
                throw;
#endif
            }
#endif
            
#if GPIE_DEVELOPMENT            
            GlobalServiceContainer.Register(new DebugService());
#endif
            
            GlobalServiceContainer.Register(new LocalizationService());
        }
        
        public static GameAnalyticsService Analytics => GlobalServiceContainer.Get<GameAnalyticsService>();
        public static DebugService Debug => GlobalServiceContainer.Get<DebugService>();
        public static ISayKitService SayKit => GlobalServiceContainer.Get<ISayKitService>();
        public static DebugLogService DebugLog => GlobalServiceContainer.Get<DebugLogService>();
        public static TimerService Timer => GlobalServiceContainer.Get<TimerService>();
        public static GameTimerService GameTimer => GlobalServiceContainer.Get<GameTimerService>();
        public static AudioService AudioService => GlobalServiceContainer.Get<AudioService>();
        public static HapticService Haptic => GlobalServiceContainer.Get<HapticService>();
        public static DateService Date => GlobalServiceContainer.Get<DateService>();
        public static LocalizationService Localization => GlobalServiceContainer.Get<LocalizationService>();
        public static HeartsService Hearts => GlobalServiceContainer.Get<HeartsService>();
        
        public static AssetPreloadService AssetPreloadService => GlobalServiceContainer.Get<AssetPreloadService>();
        public static CatalogUpdateService CatalogUpdate => GlobalServiceContainer.Get<CatalogUpdateService>();
        public static AssetStatusChecker AssetStatusChecker => GlobalServiceContainer.Get<AssetStatusChecker>();

#if SAYKIT_PURCHASING
        public static PurchaseService PurchaseService => GlobalServiceContainer.Get<GamePurchaseService>();
        // public static OffersService OffersService => GlobalServiceContainer.Get<OffersService>();
#endif
    }
}
