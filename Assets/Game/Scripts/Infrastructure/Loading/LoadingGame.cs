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
using SGS.Services.Containers;
using SGS.Services.DebugLog;
using SGS.Services.Gameplay;
using SGS.Services.Ui;
using Tools.Constants;
using Tools.Debug;
using Ui.Components.Message;
using LoadingScreen;
using Services.AssetPreload;
using Ui.Screens.RateUs;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;
using VContainer.Unity;
using Game.Code.Infrastructure;
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

        // TASK-138: the host scope that owns this entry point (Bootstrap/Root, auto-registered by VContainer as
        // LifetimeScope, LifetimeScope.cs:302) and the gameplay-session scope prefab it will raise as a CHILD. The
        // prefab reference can only be serialized on a MonoBehaviour scope, so BootstrapLifetimeScope carries the
        // [SerializeField] and RegisterInstance's it for us — LoadingGame is a plain IAsyncStartable and can't.
        private readonly LifetimeScope _hostScope;
        private readonly GameplayLifetimeScope _gameplayScopePrefab;

        public LoadingGame(
            ISceneLoader sceneLoader,
            ILoadingScreen loadingScreen,
            ILogService logService,
            LifetimeScope hostScope,
            GameplayLifetimeScope gameplayScopePrefab)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            _loadingScreen = loadingScreen ?? throw new ArgumentNullException(nameof(loadingScreen));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _hostScope = hostScope ?? throw new ArgumentNullException(nameof(hostScope));
            _gameplayScopePrefab = gameplayScopePrefab ?? throw new ArgumentNullException(nameof(gameplayScopePrefab));
        }

        public async UniTask StartAsync(CancellationToken cancellationToken)
        {
            // TASK-408: remember what the Loading scene was authored with BEFORE the boot instantiates anything into it
            // (UiService's Canvas(Clone), the gameplay scope). HideLoadingSceneStaticCanvases decides by this list.
            var sceneCanvasesAtBoot = SceneCanvasSnapshot.Capture(SceneManager.GetActiveScene());

            // TASK-134 (owner 16:11): initialize SayKit HERE, at the very start of the boot — NOT from
            // [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] as before. From BeforeSceneLoad the play-loop
            // is not spinning yet and SayKit's editor bridge deadlocks the main thread on a blocking
            // SKThreadService.RunOnMainThreadWithResult (the editor boot stall chased through this whole
            // task). The prod studio project (hexa-sorter) calls SayKitInit.Init() from the loading scene's
            // Awake with the attribute commented out; our loading entry point is this VContainer
            // IAsyncStartable, so the Awake-equivalent earliest boot point is the top of StartAsync. The
            // !SayKit.isInitialized guard mirrors the reference (idempotent if init already ran).
            if (!SayKit.isInitialized)
                SayKitInit.Init();

            InitializeGameSettings();
            SetupGameResources();
            LoadTechResources();
            await ShowLoadingScreenAsync(cancellationToken);
            await LoadResources(cancellationToken);

            // Ремоут уже получен в LoadResources (EInitState.RemoteConfig), но ещё не разлит
            // по конфигам. Применяем его до выбора пресета, чтобы загрузить пресет,
            // выбранный ремоутом, а не локальным дефолтом.
            //
            // TASK-134: прогоняем оба хвостовых шага бута через RunBootStep — он именует фазу в логе (по
            // отсутствию её маркера видно, какая встала) и ТОЛЬКО В РЕДАКТОРЕ не даёт упавшей фазе тихо
            // оборвать бут. Историческая ремарка: «бут вставал ПОСЛЕ LoadResources, finished не появлялся»
            // (owner 15:38) оказалось артефактом лог-порога — маркеры Info гасились (owner 16:36); по факту
            // хвост исполняется, оба boot step и finished в логе есть. Инструментация и editor-try/catch
            // оставлены страховкой: RemoteConfigUpdated (InitWithRemote) может кинуть NRE, если ремоут-конфиг
            // не разлит, и раньше это молча гасило бут — теперь названо Error'ом. Устройство (#else) — прежнее.
            RunBootStep("RemoteConfigUpdated", SayKitInit.RemoteConfigUpdated);

            RunBootStep("InitializeGlobalServices", InitializeGlobalServices);

            // TASK-126: поднимаем app-level UI-скоуп ПРЯМО ЗДЕСЬ, на буте, а не за (незавершённой,
            // TODO ниже) игровой сценой. Это единственная точка, где Service.UI начинает быть не-null.
            RunBootStep("InitializeLocalServices", InitializeLocalServices);

            // TASK-138: raise the gameplay session HERE — BEFORE hiding the loading screen — as a CHILD scope of
            // this host inside the still-loaded Loading scene (ADR-0004, Variant B). We do NOT load a separate Game
            // scene by addressable: LoadSceneMode.Single would unload Loading and destroy the Root scope, leaving
            // GameplayLifetimeScope (a child that needs Root for IApplicationTriggers → SaveManager) parentless, and
            // would kill the Loading Main Camera that the gameplay presenters read as Camera.main. CreateChildFromPrefab
            // sets the child's parentReference to this scope itself (LifetimeScope.cs:270), so Root/Data/Service.UI —
            // all live by this point (after InitializeLocalServices, after LoadResources) — resolve normally.
            //
            // ORDER IS LOAD-BEARING (TASK-138): InitializeLocalServices (above) MUST run before this line. It creates
            // the LocalServiceContainer + UiService, and GameplayLifetimeScope now registers Service.UI as a one-way
            // app-level bridge (its GameplayInputScreenBlocker takes UiService via ctor) — that RegisterInstance(Service.UI)
            // is evaluated during Configure() inside this CreateChildFromPrefab call. Reorder these two steps and
            // Service.UI throws InvalidOperationException here. This is now a requirement, not an accident.
            _hostScope.CreateChildFromPrefab(_gameplayScopePrefab);

            // Reveal the app. Without this the DontDestroyOnLoad loading screen stays on top forever.
            await _loadingScreen.HideIfActiveAsync(cancellationToken: cancellationToken);

            // TASK-126 (owner 08:39): гасим статический ScreenSpaceOverlay-канвас сцены Loading здесь же,
            // где приложение «показано» — сразу после того, как убран DontDestroyOnLoad-лоадинг-скрин.
            HideLoadingSceneStaticCanvases(sceneCanvasesAtBoot);
#if SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR
            LogSmokeBootMarker("boot finished: gameplay scope raised, loading screen hidden");
#endif

            // app_loaded должен уходить, когда игра полностью загружена
            // и игрок может взаимодействовать с экраном (см. доку SayKit). TASK-533: в smoke-плеере идёт через
            // GlobalService.SayKit (там EmptySayKitService) — прямой вызов ушёл бы в мёртвый мост и зациклил ошибки.
#if SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR
            GlobalService.SayKit.TrackApplicationLoaded();
#else
            SayKit.trackApplicationLoaded();
#endif

            // TASK-153: приёмочная оснастка автопоказа RestartPopup снята (был вызов DevAutoShowRestartPopupAsync
            // под #if GPIE_DEVELOPMENT). Требование владельца: на входе в игру попапов не показывается. Попап
            // открывается только штатным путём — тапом по кнопке RESTART. TASK-138: до этой задачи кнопка жила
            // ТОЛЬКО в bringup-скоупе, а прод-граф её не поднимал вовсе; теперь RestartButtonPresenter
            // зарегистрирован и в прод-графе (GameplayLifetimeScope), поднятом выше как дочерний скоуп, — так что
            // штатный путь к попапу существует и в проде, а не только в bringup.
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
            // TASK-135 (folded into TASK-240): the BoostersConfig.asset now loads like the other configs, so the
            // remote A/B levers (Hint ProactiveEnabled/DailyRefillTo, coin prices) go live. The folder Assets/Game/
            // Configs is a single addressable entry (address "Configs"), so the asset resolves at this path with no
            // separate group registration. A missing/failed load leaves Data.Configs.BoostersConfig null → the
            // booster keeps running on its C# defaults (Configs.InitWithRemote already guards the null).
            AddResource<BoostersConfig>(ConfigPath.Boosters, config => Data.Configs.BoostersConfig = config);
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

            await UniTask.Yield();

            using var _ = ListPool<UniTask>.Get(out var tasks);
            foreach (var asyncLoader in _resources)
                tasks.Add(asyncLoader.Load());

#if UNITY_EDITOR
            // TASK-129: в редакторе SayKit ходит через SayKitBridgeEditor и без своего app key (TASK-064)
            // не доводит init до EInitState.Done — прод-бут висел бы здесь вечно. Ждём SayKit-состояния
            // ОГРАНИЧЕННО и, не дождавшись, продолжаем бут на локальном конфиге, ГРОМКО это объявив.
            tasks.Add(WaitForSayKitInitWithEditorTimeoutAsync(cancellationToken));

            // TASK-134: ограниченное ожидание загрузчиков в редакторе — СТРАХОВКА от зависшего бута, не фикс
            // конкретного дефекта. По факту (замер владельца 16:32) все 13 конфигов грузятся за ~10 мс и бут
            // доходит до конца; «мёртвой доставки Addressables после ~4-5 операций» НЕ существует — та картина
            // была артефактом лог-порога (DevelopmentMinLevel=Warning гасил все Info-строки, см. п. ниже и
            // LoggingAssetLoader). Настоящее зависание было в SayKit.init() из BeforeSceneLoad, блокировавшем
            // главный поток; оно снято переносом init в начало StartAsync. Ожидание остаётся как дешёвый
            // предохранитель: если какой-то загрузчик когда-нибудь зависнет, бут не встанет вечно — по
            // таймауту НЕ бросаем, называем зависшие поимённо и продолжаем на уже загруженных конфигах.
            await WaitForResourcesWithEditorTimeoutAsync(tasks, cancellationToken);
#elif SAYKIT_SMOKE_DISABLE_RUNTIME
            // TASK-533 (owner decision 2026-10-14, variant B): the smoke player never calls SayKit.init
            // (SayKitInit.cs, player stub — no own SayKit app/Firebase yet, TASK-064), so GetInitState never moves and
            // the six waits below would hold the boot on the loading screen forever. Don't wait for states that cannot
            // come: say it loudly and boot on LOCAL config — the same semantics as the editor fallback (TASK-129).
            // Not a timeout: there is nothing to wait for. The prod player (no define) keeps the unbounded waits.
            LogSmokeSayKitSkipped();
            await tasks.WhenAll(_loadingScreen.Progress, cancellationToken: cancellationToken);
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
        // 3s (TASK-134, owner 15:38): 10s was a per-play-mode-launch tax on every prod-path check. Everything
        // that completes in the editor completes inside ~250ms, so 3s is a comfortable ceiling over the real
        // startup burst while cutting the wait-for-nothing to a third.
        private const float EditorResourceLoadTimeoutSeconds = 3f;

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
            // TASK-134 (owner 15:38, п.5): раньше отчёт печатал только счётчик «all N finished». Владелец
            // видел «all 13 finished», хотя строк «config loaded» в логе было лишь 4 — и нельзя было понять,
            // врёт ли учёт или логирование теряет завершения. Теперь отчёт печатает ИМЕНА и completed, и
            // pending. Ключ к разночтению: в LoggingAssetLoader.Load() флаг IsCompleted и строка «config
            // loaded: <path>» стоят подряд без await между ними — атомарны. Значит completed-множество ЗДЕСЬ
            // тождественно множеству строк «config loaded», которые код обязан был напечатать. РАЗГАДКА
            // (owner 16:36, замером): 9 недостающих строк не терялись — их молча гасил лог-порог
            // (LogServiceConfig.DevelopmentMinLevel=Warning отбрасывал все Info на LoadLevel с момента
            // загрузки пятого конфига). С порогом Info в логе видны все 13 «config loaded», учёт всегда был
            // честен. Список completed/pending поимённо оставлен: он и делает такое разночтение проверяемым.
            using var pendingHandle = ListPool<string>.Get(out var pending);
            using var doneHandle = ListPool<string>.Get(out var completed);
            foreach (var resource in _resources)
            {
                if (resource is not LoggingAssetLoader logging)
                    continue;

                if (logging.IsCompleted)
                    completed.Add(logging.AssetPath);
                else
                    pending.Add(logging.AssetPath);
            }

            var time = DateTime.Now;
            if (pending.Count == 0)
            {
                // Все конфиги загрузились; дедлайн выиграл из-за неконфигового ожидания (напр. SayKit init).
                // Бут всё равно продолжаем — но называем факт (и completed поимённо), чтобы таймаут не был
                // молчаливым и учёт был сверяем со строками «config loaded».
                _logService.Log(
                    LogLevel.Warning,
                    $"[{time:T}.{time.Millisecond.ToString()}] Load Configs timed out after " +
                    $"{EditorResourceLoadTimeoutSeconds.ToString()}s, but all {completed.Count.ToString()}/" +
                    $"{_resources.Count.ToString()} config loader(s) finished. Boot continues (EDITOR-ONLY). " +
                    $"Completed: {string.Join(", ", completed)}",
                    LogCategory.LoadLevel);
                return;
            }

            _logService.Log(
                LogLevel.Warning,
                $"[{time:T}.{time.Millisecond.ToString()}] Load Configs timed out after " +
                $"{EditorResourceLoadTimeoutSeconds.ToString()}s: {pending.Count.ToString()}/" +
                $"{_resources.Count.ToString()} config loader(s) still pending, boot continues WITHOUT them " +
                $"(EDITOR-ONLY). Pending: {string.Join(", ", pending)}. " +
                $"Completed ({completed.Count.ToString()}): {string.Join(", ", completed)}",
                LogCategory.LoadLevel);
        }
#endif

#if UNITY_EDITOR
        // Редакторное ограниченное ожидание SayKit init — СТРАХОВКА от зависшего бута (TASK-129/134). Плеер
        // (#else выше) не тронут: на устройстве бут по-прежнему ждёт настоящий нативный init без таймаута.
        // По факту (замер владельца 16:32, после переноса init из BeforeSceneLoad в начало StartAsync) SayKit
        // в редакторе доходит до EInitState.Done за ~200 мс и таймаут НЕ срабатывает — «SayKit в редакторе
        // без своего app key никогда не доходит до Done» больше НЕ верно (эндпоинт с пустым ключом отвечает
        // 404 за 0.2 c, init всё равно доезжает до Done). Ожидание оставлено предохранителем: если init
        // когда-нибудь зависнет, бут не встанет вечно.
        //
        // Механика таймаута: гоняем ожидание против фиксированного дедлайна UniTask.Delay через WhenAny, так
        // что таймаут — ОБЫЧНАЯ ветка кода, а не путь через отмену/исключение. Delay крутится на PlayerLoop
        // по Realtime-часам и завершится, даже если init висит. Важный урок задачи: до переноса init оба
        // механизма таймаута (CancelAfter из 129 и этот Delay) «не срабатывали» не из-за семантики UniTask, а
        // потому что SayKit.init() из BeforeSceneLoad БЛОКИРОВАЛ главный поток — PlayerLoop стоял, тикать было
        // некому. Перенос init снял блокировку, и таймаут снова работоспособен (проверено экспериментом 15:38).
        //
        // Метод инструментирован на входе и в обеих ветках исхода через _logService/LogCategory.LoadLevel
        // (единственный видимый в логе Unity канал: мост play-mode-логи не отдаёт), поэтому по этим записям
        // локализуется, где встаёт бут. Info-строки видны только при DevelopmentMinLevel=Info — на этом сгорел
        // день (owner 16:36); Warning-ветка таймаута проходила фильтр всегда.
        // 3s (TASK-134, owner 15:38): 10s был налогом на каждый вход в play-mode; всё, что успевает,
        // укладывается в ~200 мс, так что 3s — комфортный потолок над реальным init.
        private const float EditorSayKitInitTimeoutSeconds = 3f;

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

#if SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR
        // Warning, not Info: it must pass DevelopmentMinLevel=Warning (TASK-134), otherwise the skip is silent.
        private void LogSmokeSayKitSkipped()
        {
            var time = DateTime.Now;
            _logService.Log(
                LogLevel.Warning,
                $"[{time:T}.{time.Millisecond.ToString()}] SAYKIT_SMOKE_DISABLE_RUNTIME: SayKit.init is stubbed " +
                $"(state {SayKit.GetInitState}), boot does NOT wait for SayKit init states and continues on LOCAL " +
                "config, without remote. SMOKE PLAYER ONLY — see TASK-064 (own SayKit app + Firebase).",
                LogCategory.LoadLevel);
        }

        private void LogSmokeBootMarker(string marker)
        {
            var time = DateTime.Now;
            _logService.Log(
                LogLevel.Warning,
                $"[{time:T}.{time.Millisecond.ToString()}] {marker} (smoke player)",
                LogCategory.LoadLevel);
        }
#endif

        // Именованный шаг хвоста бута (TASK-134). Пишет маркер входа в фазу в тот же видимый канал
        // (_logService/LogCategory.LoadLevel), что и «Load Configs start/finished», — по отсутствию маркера
        // «finished» после маркера фазы человек/проверка локализует, ГДЕ встал бут. В редакторе фаза обёрнута
        // try/catch: одна упавшая синхронная фаза (напр. RemoteConfigUpdated → NRE по невыгруженному конфигу
        // при заглушённом SayKit) больше не гасит бут молча — называем её Error'ом и продолжаем, чтобы
        // прод-путь оставался проверяемым. На устройстве (#else) фаза зовётся напрямую — прежнее поведение,
        // любое исключение фатально, как и было.
        private void RunBootStep(string stepName, Action step)
        {
#if UNITY_EDITOR
            var enter = DateTime.Now;
            _logService.Log(
                LogLevel.Info,
                $"[{enter:T}.{enter.Millisecond.ToString()}] boot step: {stepName}",
                LogCategory.LoadLevel);

            try
            {
                step();
            }
            catch (Exception e)
            {
                var failed = DateTime.Now;
                _logService.Log(
                    LogLevel.Error,
                    $"[{failed:T}.{failed.Millisecond.ToString()}] boot step FAILED: {stepName} — " +
                    $"{e.GetType().Name}: {e.Message}. EDITOR-ONLY: boot continues past it.",
                    LogCategory.LoadLevel);
            }
#else
            // Устройство: фаза зовётся напрямую, без try/catch — поведение плеера не меняется. TASK-533: в smoke-плеере
            // маркер фазы пишется Warning'ом — иначе по logcat нельзя сказать, где встал прод-бут на устройстве.
#if SAYKIT_SMOKE_DISABLE_RUNTIME
            LogSmokeBootMarker($"boot step: {stepName}");
#endif
            step();
#endif
        }

        // TASK-126: единственный app-lifetime bootstrap локального сервис-слоя. Service.UI читает
        // ИМЕННО LocalServiceContainer (Service.cs) — НЕ VContainer-граф: RegisterEntryPoint<UiService>()
        // в GameplayLifetimeScope клал сервис в другой контейнер, из которого Service.UI не читает (и был
        // инертен — UiService не реализует ни одного VContainer entry-point-интерфейса), поэтому
        // Service.UI всегда возвращал null. Мост — ровно та же форма, что GlobalService.Create
        // (GlobalService.cs:23): Create() один раз + Register сервисов. UiService конструируется голым
        // new (нет ctor-зависимостей); Register зовёт его Init(). TASK-408: Init() НЕ откладывает работу до
        // первого Show, как утверждалось здесь раньше, — InitializeAsync() вызывается сразу, а ToAsyncLazy(task)
        // лишь оборачивает уже бегущую задачу (UniTaskExtensions.ToAsyncLazy → new AsyncLazy(task)). Значит
        // Canvas(Clone) начинает инстанцироваться ЗДЕСЬ и живёт без worldCamera, пока ждёт камеру, — ровно в это
        // окно и попадал HideLoadingSceneStaticCanvases. Контейнер создаём РОВНО ОДИН
        // РАЗ на app-lifetime: повторный Create() сделал бы DeInit старого инстанса и стёр реестр
        // (LocalServiceContainer.cs) — поэтому пакетный per-level LevelController.Create (мёртвый путь,
        // подклассов нет) сюда НЕ тащим.
        //
        // PauseService регистрируется ВМЕСТЕ с UiService не как scope-крип, а по необходимости: сам факт
        // Create() делает Service.IsReady (== LocalServiceContainer.IsReady()) == true, а глобальный
        // покадровый тикер GameTimerService (GlobalService, ServiceAddapter.cs) гейтится ровно этим флагом
        // — `if (Service.IsReady && !Service.Pause.IsPaused)`. До TASK-126 контейнер не создавался, IsReady
        // был false и гейт короткозамыкался; как только контейнер «готов», тикер начинает дёргать
        // Service.Pause КАЖДЫЙ кадр — без регистрации PauseService это NRE на каждом Update/LateUpdate
        // (поймано play-mode-прогоном этой задачи). Контракт «контейнер готов ⇒ Service.Pause резолвится»
        // обязан держаться. PauseService — без ctor-зависимостей, Init() пустой. Прочие локальные сервисы
        // (Pool/TimeScale/Messages/RewardedState) живого IsReady-гейтед потребителя пока НЕ имеют
        // (единственный Service.IsReady в дереве — GameTimerService) → остаются tech-debt, вне scope.
        private void InitializeLocalServices()
        {
            LocalServiceContainer.Create();
            LocalServiceContainer.Register(new UiService());
            LocalServiceContainer.Register(new PauseService());
        }

        // TASK-126 (owner 08:39): статический канвас сцены Loading несёт полноэкранный Background в
        // RenderMode.ScreenSpaceOverlay. Overlay в Unity композитится ПОСЛЕ всех камер и поверх любого
        // camera-space-канваса НЕЗАВИСИМО от sortingOrder, поэтому этот белый лист закрывал RestartPopup,
        // который наш UI-канвас (Canvas.prefab = ScreenSpaceCamera, _useSeparateCanvasCamera:1) рисует
        // отдельной overlay-КАМЕРОЙ — отсюда «попап в иерархии есть, а экран белый».
        //
        // Почему это ФИКС, а не костыль: этот канвас — статический остаток выпотрошенного шаблона, который
        // никто не скрывает (это НЕ лоадинг-скрин: тот живёт в DontDestroyOnLoad и уже корректно спрятан
        // HideIfActiveAsync выше; grep LoadingScreen по Loading.unity == 0), поэтому он просто дефект.
        // TASK-138: под Вариантом B (ADR-0004) прод-путь НЕ грузит отдельную игровую сцену — сессия
        // поднимается дочерним скоупом ВНУТРИ живой Loading, сцена не выгружается. Значит эта правка —
        // НЕСУЩАЯ, а не no-op: без неё белый overlay-лист остался бы и закрывал уровень с попапом. (Прежняя
        // премиса «в проде сцена выгрузится LoadSceneMode.Single, и правка станет no-op» относилась к
        // отвергнутому Варианту A и более не верна.)
        //
        // Ищем БЕЗ хардкода имён (owner п.1): гасим ScreenSpaceOverlay-канвасы из снимка, снятого в начале
        // StartAsync, — то есть только объекты, с которыми сцена была ЗАГРУЖЕНА (owner п.2): (а) лоадинг-скрин
        // не трогается — он в псевдо-сцене DontDestroyOnLoad, а не в активной; (б) наш Canvas(Clone) не
        // трогается — он инстанцирован UiService ПОСЛЕ снимка и в него не попал. TASK-408: прежняя гарантия (б)
        // «он ScreenSpaceCamera и под фильтр renderMode не попадает» была ложной — пока UiService ждёт
        // инстанцирования камеры, геттер renderMode у канваса без worldCamera отдаёт ScreenSpaceOverlay, и шаг
        // гасил корень UI (лог «Hid 2 … [Canvas, Canvas(Clone)]», TASK-401). Гасим сам GameObject
        // (SetActive(false)), а не только Canvas.enabled — чтобы отсутствие рендера было проверяемо
        // (unity_find_gameobjects покажет active:false, owner п.5). Метод идемпотентен и безопасен при нуле
        // подходящих канвасов.
        private void HideLoadingSceneStaticCanvases(SceneCanvasSnapshot sceneCanvasesAtBoot)
        {
            var scene = SceneManager.GetActiveScene();

            using var handle = ListPool<string>.Get(out var hidden);
            sceneCanvasesAtBoot.HideOverlayCanvases(hidden);

            var time = DateTime.Now;
            _logService.Log(
                LogLevel.Info,
                $"[{time:T}.{time.Millisecond.ToString()}] Hid {hidden.Count.ToString()} static " +
                $"ScreenSpaceOverlay canvas(es) of scene '{scene.name}' (template remnant, NOT the " +
                $"DontDestroyOnLoad loading screen). Hidden: [{string.Join(", ", hidden)}]",
                LogCategory.LoadLevel);
        }

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
