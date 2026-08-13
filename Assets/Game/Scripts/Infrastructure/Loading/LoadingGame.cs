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
            AddResource<AnimationsConfig>(ConfigPath.Animations, config => Data.Configs.Animations = config);
            AddResource<AudioConfig>(ConfigPath.Audio, config => SayData.Configs.Audio = config);
            AddResource<UiPrefabs>(ConfigPath.Ui, config => SayData.Configs.Ui = config);
            AddResource<RateUsConfig>(ConfigPath.RateUs, config => Data.Configs.RateUsConfig = config);
            AddResource<CoreConfig>(ConfigPath.Core, config => Data.Configs.Core = config);
            AddResource<LogServiceConfig>(ConfigPath.LogServiceConfig, config => Data.Configs.LogServiceConfig = config);
            AddResource<MessagesViewConfig>(ConfigPath.Messages, config => Data.Configs.MessagesConfig = config);
            AddResource<StartConfig>(ConfigPath.Start, config => Data.Configs.Start = config);
            AddResource<RewardedConfig>(ConfigPath.Rewarded, config => Data.Configs.RewardedConfig = config);
            AddResource<HeartConfig>(ConfigPath.Heart, config => Data.Configs.HeartConfig = config);

            AddResource<InputConfig>(ConfigPath.InputConfig, config => Data.Configs.InputConfig = config);

            AddResource<NoAdsOfferConfig>(ConfigPath.NoAdsConfig, config => Data.Configs.NoAdsOfferConfig = config);
            AddResource<AssetPreloadConfig>(ConfigPath.AssetPreloadConfig, config => Data.Configs.AssetPreloadConfig = config);

#if SAYKIT_PURCHASING
            AddResource<PurchasesConfig>(ConfigPath.Purchase, config => Data.Configs.Purchase = config);
            AddResource<StoreVisualConfig>(ConfigPath.StoreVisual, config => Data.Configs.StoreVisual = config);
            AddResource<StoreConfig>(ConfigPath.StoreConfig, config => Data.Configs.StoreConfig = config);
            AddResource<StoreItemsConfig>(ConfigPath.StoreItemsConfig, config => Data.Configs.StoreItemsConfig = config);
#endif
        }

        // Каждый загрузчик оборачивается проектной LoggingAssetLoader (TASK-134): пакетный
        // AssetUniTaskAsyncLoader не трогаем, а старт/завершение каждого Load() логируем с его assetPath,
        // чтобы висящий определялся по паре «config load start» без «config loaded».
        private void AddResource<T>(string assetPath, Action<T> assignAction) where T : UnityEngine.Object
        {
            var loader = new AssetUniTaskAsyncLoader<T>(assetPath, assignAction);
            _resources.Add(new LoggingAssetLoader(loader, assetPath, _logService));
        }

        private async UniTask LoadResources(CancellationToken cancellationToken)
        {
            var time = DateTime.Now;
            _logService.Log(LogLevel.Info, $"[{time:T}.{time.Millisecond.ToString()}] Load Configs start", LogCategory.LoadLevel);

#if UNITY_EDITOR
            // TASK-134 (ВРЕМЕННО, диагностика — не фикс): раз в секунду печатать injected/utick/frame,
            // чтобы на столле бута отличить «раннеры UniTask выпали» от «есть, но не тикают» от «живы,
            // дело в провайдере». Самоснимается на выходе из play-mode. Удаляется прогоном фикса.
            EditorBootLoopProbe.Begin(_logService);
#endif

            await UniTask.Yield();

            using var _ = ListPool<UniTask>.Get(out var tasks);
            foreach (var asyncLoader in _resources)
                tasks.Add(asyncLoader.Load());

#if UNITY_EDITOR
            // TASK-129: в редакторе SayKit ходит через SayKitBridgeEditor и без своего app key (TASK-064)
            // не доводит init до EInitState.Done — прод-бут висел бы здесь вечно. Ждём SayKit-состояния
            // ОГРАНИЧЕННО и, не дождавшись, продолжаем бут на локальном конфиге, ГРОМКО это объявив.
            tasks.Add(WaitForSayKitInitWithEditorTimeoutAsync(cancellationToken));

            // TASK-134: и сами загрузчики конфигов в редакторе ждём ОГРАНИЧЕННО — Addressables-доставка
            // в интерактивном редакторе молча умирает после ~4-5 операций (замеры владельца 12:47–13:40),
            // tasks.WhenAll висел бы здесь вечно и бут не доходил до конца. По таймауту НЕ бросаем: называем
            // зависшие загрузчики поимённо и продолжаем бут, чтобы уже загруженные конфиги были доступны.
            await WaitForResourcesWithEditorTimeoutAsync(tasks, cancellationToken);
#else
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Platform, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.RemoteConfig, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Consent, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.IDFA, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState >= EInitState.Gdpr, cancellationToken: cancellationToken));
            tasks.Add(UniTask.WaitUntil(() => SayKit.GetInitState == EInitState.Done, cancellationToken: cancellationToken));

            await tasks.WhenAll(_loadingScreen.Progress, cancellationToken: cancellationToken);
#endif
        }

#if UNITY_EDITOR
        // Редакторный лимит ожидания загрузчиков конфигов (TASK-134). На устройстве (#else в LoadResources)
        // ожидание без таймаута НЕ тронуто. Гоняем tasks.WhenAll против фиксированного дедлайна тем же приёмом
        // WhenAny + UniTask.Delay, что и SayKit init выше: таймаут — ОБЫЧНАЯ ветка кода, а не путь через
        // отмену/исключение (прежний fire-and-forget сторож на UniTask.Delay(12s).Forget() за 22 c play-mode
        // ни разу не выстрелил — TASK-134, замеры 03; детерминированная ветка WhenAny надёжнее). Delay крутится
        // на PlayerLoop по Realtime-часам и завершится, даже если Addressables-доставка мертва.
        private const float EditorResourceLoadTimeoutSeconds = 10f;

        private async UniTask WaitForResourcesWithEditorTimeoutAsync(
            IEnumerable<UniTask> tasks, CancellationToken cancellationToken)
        {
            // Прогресс сохраняем как на устройстве; проигравшую ветку WhenAny не отменяем — осиротевший
            // поллер продолжит крутиться до выхода из play-mode, где его снимет cancellationToken (та же
            // модель, что у WaitForSayKitInitWithEditorTimeoutAsync).
            var allTasks = tasks.WhenAll(_loadingScreen.Progress, cancellationToken: cancellationToken);
            var deadline = UniTask.Delay(
                TimeSpan.FromSeconds(EditorResourceLoadTimeoutSeconds),
                DelayType.Realtime,
                cancellationToken: cancellationToken);

            var winnerIndex = await UniTask.WhenAny(allTasks, deadline);
            if (winnerIndex == 0)
                return; // все загрузчики (и SayKit-ожидание) завершились в срок — бут идёт дальше

            ReportUnfinishedResourceLoaders();
        }

        // Warning с ПУТЯМИ незавершившихся загрузчиков (не только счётчиком — имена нам уже стоили прогона,
        // TASK-134). Факт завершения отдаёт LoggingAssetLoader.IsCompleted. Канал — _logService/
        // LogCategory.LoadLevel: единственный, видимый в логе Unity (мост play-mode-логи не отдаёт, а Debug.*
        // режется filterLogType, см. InitializeGameSettings). Именно молчание превращало один висящий
        // Addressables-загрузчик в вечное «загружается».
        private void ReportUnfinishedResourceLoaders()
        {
            using var _ = ListPool<string>.Get(out var unfinished);
            foreach (var resource in _resources)
            {
                if (resource is LoggingAssetLoader logging && !logging.IsCompleted)
                    unfinished.Add(logging.AssetPath);
            }

            var time = DateTime.Now;
            if (unfinished.Count == 0)
            {
                // Все конфиги загрузились; дедлайн выиграл из-за неконфигового ожидания (напр. SayKit init).
                // Бут всё равно продолжаем — но называем факт, чтобы таймаут не был молчаливым.
                _logService.Log(
                    LogLevel.Warning,
                    $"[{time:T}.{time.Millisecond.ToString()}] Load Configs timed out after " +
                    $"{EditorResourceLoadTimeoutSeconds.ToString()}s, but all {_resources.Count.ToString()} config " +
                    "loader(s) finished. Boot continues (EDITOR-ONLY).",
                    LogCategory.LoadLevel);
                return;
            }

            _logService.Log(
                LogLevel.Warning,
                $"[{time:T}.{time.Millisecond.ToString()}] Load Configs timed out after " +
                $"{EditorResourceLoadTimeoutSeconds.ToString()}s: {unfinished.Count.ToString()}/" +
                $"{_resources.Count.ToString()} config loader(s) still pending, boot continues WITHOUT them " +
                $"(EDITOR-ONLY): {string.Join(", ", unfinished)}",
                LogCategory.LoadLevel);
        }
#endif

#if UNITY_EDITOR
        // Редакторный шорткат прод-бута (TASK-129). Плеер (#else выше) не тронут: на устройстве бут
        // по-прежнему ждёт настоящий нативный init без таймаута. Здесь ждём EInitState.Done ОГРАНИЧЕННО —
        // гоняем ожидание против фиксированного дедлайна UniTask.Delay через WhenAny, поэтому таймаут это
        // ОБЫЧНАЯ ветка кода, а не путь через отмену/исключение: он срабатывает независимо от того, как
        // UniTask прокидывает cancellation сквозь WaitUntil. Прежний вариант (linked CancellationTokenSource
        // + CancelAfter, отмена → catch OperationCanceledException) эмпирически НЕ срабатывал — за 22 c при
        // таймауте 10 c catch не исполнился ни разу (TASK-129, замер человека 11:06). Delay крутится на
        // PlayerLoop по Realtime-часам (Stopwatch) и завершится, даже если init висит.
        //
        // Метод инструментирован на входе и в обеих ветках исхода: _logService — единственный канал,
        // видимый в логе Unity (мост play-mode-логи не отдаёт), поэтому по этим записям человек локализует,
        // где встаёт бут. Если после «did not reach» маркер «Load Configs finished» так и не появится —
        // держит WhenAll кто-то из ресурс-загрузчиков (_resources), это отдельная находка, а не таймаут.
        // Настоящая причина неполного init в редакторе — отсутствие своего app key, см. TASK-064.
        private const float EditorSayKitInitTimeoutSeconds = 10f;

        private async UniTask WaitForSayKitInitWithEditorTimeoutAsync(CancellationToken cancellationToken)
        {
            var enter = DateTime.Now;
            _logService.Log(
                LogLevel.Info,
                $"[{enter:T}.{enter.Millisecond.ToString()}] Waiting for SayKit init " +
                $"(editor, max {EditorSayKitInitTimeoutSeconds}s), current state {SayKit.GetInitState}",
                LogCategory.LoadLevel);

            // Проигравшая ветка WhenAny не отменяется — осиротевший WaitUntil (если победит дедлайн)
            // продолжит дешёвый поллинг до выхода из play-mode, где cancellationToken его снимет.
            var initReached = UniTask.WaitUntil(
                () => SayKit.GetInitState == EInitState.Done, cancellationToken: cancellationToken);
            var deadline = UniTask.Delay(
                TimeSpan.FromSeconds(EditorSayKitInitTimeoutSeconds),
                DelayType.Realtime,
                cancellationToken: cancellationToken);

            var winnerIndex = await UniTask.WhenAny(initReached, deadline);

            var done = DateTime.Now;
            if (winnerIndex == 0)
            {
                _logService.Log(
                    LogLevel.Info,
                    $"[{done:T}.{done.Millisecond.ToString()}] SayKit init reached {SayKit.GetInitState}",
                    LogCategory.LoadLevel);
            }
            else
            {
                _logService.Log(
                    LogLevel.Warning,
                    $"[{done:T}.{done.Millisecond.ToString()}] SayKit init did not reach EInitState.Done in " +
                    $"{EditorSayKitInitTimeoutSeconds}s (stuck at {SayKit.GetInitState}). EDITOR-ONLY: boot continues on " +
                    "LOCAL config, without remote. Root cause is the missing own app key — see TASK-064. " +
                    "Player build is unaffected (waits for real native init, no timeout).",
                    LogCategory.LoadLevel);
            }
        }
#endif

        private void InitializeGlobalServices()
        {
            GlobalService.Create();
#if GPIE_DEVELOPMENT
            GlobalService.Debug.OnDebugMenuShown = GameDebugMenu.Create;
#endif
#if SAYKIT_PURCHASING
            Data.Configs.Purchase.VerifySettings(Data.Configs.StoreVisual);
#endif

            // Мирроринг маркера старта (см. LoadResources) в тот же канал _logService/LogCategory.LoadLevel:
            // это единственная запись «бут дошёл до конца», попадающая в лог Unity. Канал GlobalService.DebugLog
            // (DebugLogType.ApplicationFlow) в лог Unity не пишет и годится только для внутриигрового оверлея —
            // им нельзя наблюдать завершение бута ни человеку, ни play-mode проверке.
            var time = DateTime.Now;
            _logService.Log(LogLevel.Info, $"[{time:T}.{time.Millisecond.ToString()}] Load Configs finished", LogCategory.LoadLevel);
            GlobalService.DebugLog.Log("Load Configs finished", DebugLogType.ApplicationFlow);
        }
    }
}
