using System;
using System.Collections.Generic;
using System.Threading;
using Common.Extensions;
using Common.SceneLoader;
using Cysharp.Threading.Tasks;
using DataStorage;
using DynamicDebugMenu;
using SayKitInternal;
using Scripts.Audio;
using Services;
using SGS.Configs;
using SGS.Data;
using SGS.Infrastructure.Loading;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using SGS.Services.DebugLog;
using Tools.Constants;
using Tools.Debug;
using Ui.Components.Message;
using LoadingScreen;
using Services.AssetPreload;
using Ui.Screens.RateUs;
using UnityEngine;
using UnityEngine.Pool;
using VContainer.Unity;
using Game.Configs;
using Game.Services.LogService;
using Gameplay.Configs;

#if SAYKIT_PURCHASING
using SGS.Store.Purchases;
#endif

namespace Infrastructure.Loading
{
    public class LoadingGame : IAsyncStartable
    {
        private readonly List<IAssetUniTaskAsyncLoader> _resources = new ();

        private readonly ISceneLoader _sceneLoader;
        private readonly ILoadingScreen _loadingScreen;
        private readonly ILogService _logService;

        public LoadingGame(
            ISceneLoader sceneLoader,
            ILoadingScreen loadingScreen,
            ILogService logService)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            _loadingScreen = loadingScreen ?? throw new ArgumentNullException(nameof(loadingScreen));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async UniTask StartAsync(CancellationToken cancellationToken)
        {
            InitializeGameSettings();
            SetupGameResources();
            LoadTechResources();
            await ShowLoadingScreenAsync(cancellationToken);
            await LoadResources(cancellationToken);

            // Ремоут уже получен в LoadResources (EInitState.RemoteConfig), но ещё не разлит
            // по конфигам. Применяем его до выбора пресета, чтобы загрузить пресет,
            // выбранный ремоутом, а не локальным дефолтом.
            SayKitInit.RemoteConfigUpdated();

            InitializeGlobalServices();

            // TODO: load the new game's main scene here — BEFORE hiding the loading screen — e.g.:
            //     await _sceneLoader.LoadSceneByAddressableAsync(SceneInBuild.Game, _loadingScreen.Progress);
            // (also add the Game scene to ScenesConfig.asset with a valid addressable reference).

            // Reveal the app. Without this the DontDestroyOnLoad loading screen stays on top forever.
            await _loadingScreen.HideIfActiveAsync(cancellationToken: cancellationToken);

            // app_loaded должен уходить, когда игра полностью загружена
            // и игрок может взаимодействовать с экраном (см. доку SayKit).
            SayKit.trackApplicationLoaded();
        }

        private UniTask ShowLoadingScreenAsync(CancellationToken cancellationToken)
        {
            return _loadingScreen.ShowIfNotActiveAsync(cancellationToken: cancellationToken);
        }

        private static void InitializeGameSettings()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            PlayerPrefsCollection.Init(new PrefsStorageBackend());
            Data.Create();
            SayData.Create();
            WrapperUnityLogService.ApplyCategoryMask(Data.Debug.LogCategoryMask.Value);
            SayData.Configs.DebugItems = Resources.Load<DebugItemsConfig>(ConfigPath.DebugItems);

#if GPIE_DEVELOPMENT
            SRDebug.Init();
            Debug.unityLogger.filterLogType = LogType.Log;
#else
            Debug.unityLogger.filterLogType = LogType.Error;
#endif
        }

        private static void LoadTechResources()
        {
            Data.App.Version = Application.version;

            var buildNumber = Resources.Load<TextAsset>("build_number");
            if (buildNumber != null)
            {
                Data.App.BuildNumber = buildNumber.text.Trim();
            }

            var gutCommit = Resources.Load<TextAsset>("git_commit");
            if (gutCommit != null)
            {
                Data.App.GitCommit = gutCommit.text.Trim();
            }

        }

        private void SetupGameResources()
        {
            _resources.Add(new AssetUniTaskAsyncLoader<AnimationsConfig>(ConfigPath.Animations, config => Data.Configs.Animations = config));
            _resources.Add(new AssetUniTaskAsyncLoader<AudioConfig>(ConfigPath.Audio, config => SayData.Configs.Audio = config));
            _resources.Add(new AssetUniTaskAsyncLoader<UiPrefabs>(ConfigPath.Ui, config => SayData.Configs.Ui = config));
            _resources.Add(new AssetUniTaskAsyncLoader<RateUsConfig>(ConfigPath.RateUs, config => Data.Configs.RateUsConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<CoreConfig>(ConfigPath.Core, config => Data.Configs.Core = config));
            _resources.Add(new AssetUniTaskAsyncLoader<LogServiceConfig>(ConfigPath.LogServiceConfig, config => Data.Configs.LogServiceConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<MessagesViewConfig>(ConfigPath.Messages, config => Data.Configs.MessagesConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<StartConfig>(ConfigPath.Start, config => Data.Configs.Start = config));
            _resources.Add(new AssetUniTaskAsyncLoader<RewardedConfig>(ConfigPath.Rewarded, config => Data.Configs.RewardedConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<HeartConfig>(ConfigPath.Heart, config => Data.Configs.HeartConfig = config));

            _resources.Add(new AssetUniTaskAsyncLoader<InputConfig>(ConfigPath.InputConfig, config => Data.Configs.InputConfig = config));

            _resources.Add(new AssetUniTaskAsyncLoader<NoAdsOfferConfig>(ConfigPath.NoAdsConfig, config => Data.Configs.NoAdsOfferConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<AssetPreloadConfig>(ConfigPath.AssetPreloadConfig, config => Data.Configs.AssetPreloadConfig = config));

#if SAYKIT_PURCHASING
            _resources.Add(new AssetUniTaskAsyncLoader<PurchasesConfig>(ConfigPath.Purchase, config => Data.Configs.Purchase = config));
            _resources.Add(new AssetUniTaskAsyncLoader<StoreVisualConfig>(ConfigPath.StoreVisual, config => Data.Configs.StoreVisual = config));
            _resources.Add(new AssetUniTaskAsyncLoader<StoreConfig>(ConfigPath.StoreConfig, config => Data.Configs.StoreConfig = config));
            _resources.Add(new AssetUniTaskAsyncLoader<StoreItemsConfig>(ConfigPath.StoreItemsConfig, config => Data.Configs.StoreItemsConfig = config));
#endif
        }

        private async UniTask LoadResources(CancellationToken cancellationToken)
        {
            var time = DateTime.Now;
            _logService.Log(LogLevel.Info, $"[{time:T}.{time.Millisecond.ToString()}] Load Configs start", LogCategory.LoadLevel);
            await UniTask.Yield();

            using var _ = ListPool<UniTask>.Get(out var tasks);
            foreach (var asyncLoader in _resources)
                tasks.Add(asyncLoader.Load());

            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Platform, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.RemoteConfig, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Consent, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.IDFA, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Gdpr, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState == EInitState.Done, cancellationToken: cancellationToken));

            await tasks.WhenAll(_loadingScreen.Progress, cancellationToken: cancellationToken);
        }

        private static void InitializeGlobalServices()
        {
            GlobalService.Create();
#if GPIE_DEVELOPMENT
            GlobalService.Debug.OnDebugMenuShown = GameDebugMenu.Create;
#endif
#if SAYKIT_PURCHASING
            Data.Configs.Purchase.VerifySettings(Data.Configs.StoreVisual);
#endif

            GlobalService.DebugLog.Log("Load Configs finished", DebugLogType.ApplicationFlow);
        }
    }
}
