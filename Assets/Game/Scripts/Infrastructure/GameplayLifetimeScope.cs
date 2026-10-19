using System.Collections.Generic;
using DataStorage;
using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.DI;
#if GPIE_DEVELOPMENT
using Gameplay.Dev;
#endif
using Gameplay.PlayScreen;
using Gameplay.Sequence.DI;
using Gameplay.Tutorial;
using Gameplay.View;
using Services;
using Services.Gameplay;
using SGS.Services.Date_Time;
using SGS.Services.Gameplay;
using SGS.Services.Pools;
using VContainer;
using VContainer.Unity;
using Game.Infrastructure;
using Game.Infrastructure.Saving;
using Game.Services.Input;
#if SAYKIT_PURCHASING
using SGS.Store.Purchases;
#endif

namespace Game.Code.Infrastructure
{
    /// <summary>
    /// Per-session (gameplay) DI scope. TileSort gameplay registrations were removed;
    /// this is the skeleton the new game hangs its GameModel / controllers / state machine on.
    /// </summary>
    public class GameplayLifetimeScope : LifetimeScope
    {
        private readonly List<IInstaller> _installers = new()
        {
            new SequenceInstaller(),
            new GameplayCoreInstaller(),
        };

        protected override void Configure(IContainerBuilder builder)
        {
            foreach (var installer in _installers)
                installer.Install(builder);

            builder.Register<IInputService, InputService>(Lifetime.Singleton);
            builder.Register<IInputBlockersHandler, InputBlockersHandler>(Lifetime.Singleton);

            // Production input source (moved out of GameplayCoreInstaller, RFC §2.3): IInputService-backed.
            // Wrapped by the tutorial input decorator (TASK-049): register the concrete source by itself, then
            // bind IGameplayInputSource to the decorator that consults the tutorial input gate. Prod is the easy
            // case (the concrete is not an entry point), so the swap is a plain pair (human 15:21 DI note).
            builder.Register<InputServiceGameplayInputSource>(Lifetime.Singleton);
            builder.Register<IGameplayInputSource>(
                resolver => new TutorialGatedInputSource(
                    resolver.Resolve<InputServiceGameplayInputSource>(),
                    resolver.Resolve<ITutorialInputGate>()),
                Lifetime.Singleton);

            builder.RegisterInstance(Data.Configs.Core).AsSelf();

            // Persistence (TASK-044): the campaign pointer is the SDK-canon Data.Meta.CurrentLevel via
            // MetaCampaignStore; the in-level snapshot contour is gone. SaveManager is repurposed into the
            // lifecycle prefs-flush (pause/focus/quit → SaveDataIfChanged).
            builder.Register<ICampaignStore, MetaCampaignStore>(Lifetime.Singleton);
            builder.RegisterEntryPoint<SaveManager>();

            // Prod level-resolution flow (TASK-154 → TASK-157): CampaignProgressionController drives its
            // transition through ILevelResolutionFlow; prod binds GatedResolutionFlow — a Lost shows the LoseScreen
            // and a Won shows the WinScreen, each after a settle-beat → panel → wait for the tap → transition.
            // (Was LoseResolutionFlow, which only gated Lost and auto-advanced a Won; before that
            // ImmediateResolutionFlow.) PlayScreenConfig (the settle-beat numbers) is registered here because the
            // flow needs it. A double bind of the interface is disallowed, so this single context-dependent bind is
            // the only correct option; bringup keeps its own SequencedResolutionFlow. The flow never duplicates
            // "won ? advance : reload" — that stays the single source of truth in CampaignProgressionController.
            builder.RegisterInstance(new PlayScreenConfig());
            builder.Register<ILevelResolutionFlow, GatedResolutionFlow>(Lifetime.Singleton);

            // Play screen (TASK-138 → TASK-154): the always-on RESTART button + its confirm popup + the lose screen
            // live in the PROD graph, raised as a child scope by LoadingGame (CreateChildFromPrefab). No duplicate
            // source of truth: each scope owns its own bindings and prod/bringup never coexist at runtime (same
            // pattern as the twice-installed SequenceInstaller). ICampaignRestart is already bound by
            // GameplayCoreInstaller. PlayScreenInteraction.IsBusy is now driven by GatedResolutionFlow (true while the
            // win/lose panel is pending/up), so RESTART is disabled during a resolution and cannot race the transition.
            // The full WIN/LOSE banner (SequencedResolutionFlow + IPlayScreenView) is still NOT part of prod — only
            // the win/lose screens are.
            builder.Register<PlayScreenInteraction>(Lifetime.Singleton);
            builder.RegisterEntryPoint<RestartButtonPresenter>();
            // TASK-562: the settings gear, top-left. Built hidden; SettingsGearBinder (TASK-566) hands it the action that
            // opens the settings popup and so shows it — .AsSelf() so the binder can inject it. Order-free: Bind before
            // CreateButton only stores the action. Prod scope only: bringup has no settings screen.
            builder.RegisterEntryPoint<SettingsButtonPresenter>().AsSelf();
            builder.RegisterEntryPoint<SettingsGearBinder>();
            // TASK-398: the HUD buttons are Overlay canvases and draw ABOVE every UiService screen whatever their
            // sortingOrder, so they hide themselves while a screen is up. This bridge feeds that flag
            // (PlayScreenInteraction.IsCoveredByScreen) from the UiService show/hide events; UiService is bound below
            // (RegisterSessionServices). Prod-only: bringup shows no UiService screens, its flag stays false.
            builder.RegisterEntryPoint<HudScreenCoverBridge>();

            // TASK-237: Hint booster — prod-only (the sim never builds these; they are not in GameplayCoreInstaller,
            // so the "sim does not create presenters/boosters" invariant holds, exactly like the tutorial). The
            // charge store + faucet are plain Singletons; LevelFailStreak is an entry point (needs Start) also
            // resolvable by concrete type for the presenter; the button is an entry point like RestartButton.
            builder.Register<IBoosterChargeStore, MetaBoosterChargeStore>(Lifetime.Singleton);
            builder.Register<HintBoosterService>(Lifetime.Singleton);
            builder.RegisterEntryPoint<LevelFailStreak>().AsSelf();
            builder.RegisterEntryPoint<HintBoosterPresenter>();

            // TASK-612: the TASK-240 coin economy (level-reward faucet, start-gold seed, ×2 stub doubler) is gone
            // with its registrations — the game has no currency; a win shows three stars instead (design 01 §2).

            // TASK-164: вуд-арт-слой художника (ветка art/andrei-v1). Ветка вешала эти три презентера
            // ТОЛЬКО в ShapeFillBringupLifetimeScope, но bringup-сцены в билде нет — единственный enabled-путь
            // это Loading → ЭТОТ прод-скоуп (child-scope, TASK-138/ADR-0004). Без дорегистрации здесь собранный
            // APK остался бы grey-box при зелёном компайле. Все три резолвятся в прод-графе: Backdrop/Foliage
            // без ctor-зависимостей (WoodArt/WoodTuning/Camera.main — статика), Palette тянет GameModel, который
            // регистрирует GameplayCoreInstaller (общий с bringup). Скин RESTART-кнопки (с TASK-562 — кремовый
            // die-cut, не вуд) живёт внутри RestartButtonPresenter выше. Порядок регистрации не несущий: каждый презентер
            // подгоняется под камеру покадрово / перекрашивает детали событием (см. их докстринги).
            builder.RegisterEntryPoint<WoodBackdropPresenter>();
            builder.RegisterEntryPoint<WoodLevelPalettePresenter>();
            builder.RegisterEntryPoint<WoodFoliagePresenter>();

#if GPIE_DEVELOPMENT
            // TASK-113: deliver the prod GameDebugMenu "Reset Progress"/"Go to Level" restart request (an
            // otherwise-unsubscribed GlobalService.Debug.OnDebugMenuClose) to the campaign owner + reload in place.
            // Prod scope only — this is where the index owner (CampaignProgressionController, via GameplayCoreInstaller)
            // and PlayScreenInteraction both live; bringup gates its own menu locally (BringupDebugMenuHost).
            builder.RegisterEntryPoint<GameDebugMenuRestartBridge>();

            // TASK-172: visible, keyboard-free entry into the cheats from the prod Loading path. Owner directive
            // (2026-08-21): the game boots only through Loading.unity, but there the SayKit DDM menu opens only by the
            // undiscoverable shake (Escape is legacy-input-only; the project runs the new Input System). This adds a
            // corner DEV button + backquote that flip SayData.Debug.DebugMenuShown — the same observable seam shake
            // flips — so DebugService raises the existing GameDebugMenu; plus an always-visible level indicator and a
            // RESTART-hide while the menu is open (fixes the TASK-117 z-order collision on the prod path). Prod scope
            // only, same reason as the bridge above: the shared GameplayCoreInstaller would raise a second menu in
            // bringup and needs the prod-only DebugService/SayData graph bringup does not hold.
            builder.RegisterEntryPoint<GameDebugMenuEntryPoint>();

            // TASK-533: per-fixed-step [STEPLOG] pose logger (TASK-070) in the prod graph. The Android dev APK now
            // boots through Loading.unity, so a bringup-only registration left the device build without the
            // sim↔device parity log. Read-only (GameController.StepObserver hook) → physics/poseHash unchanged.
            // Lives in both scopes, never twice at runtime: prod and bringup do not coexist, and the headless sim
            // news its own logger (SimRun). Deps (GameController, GameModel, ShapeFillTuning) — GameplayCoreInstaller.
            builder.RegisterEntryPoint<StepPoseLogger>();
#endif

            // Tutorial system prod impls (TASK-049): completion on the SDK-canon Data.Meta.CompletedTutorials,
            // gate on Data.Debug.DisableTutorial, funnel via the SayKit tutorial analytics. Context-independent
            // parts (input gate, view stub, definition, runner) live in GameplayCoreInstaller.
            builder.Register<ITutorialProgressStore, MetaTutorialProgressStore>(Lifetime.Singleton);
            builder.Register<ITutorialGate, ProdTutorialGate>(Lifetime.Singleton);
            builder.Register<ITutorialAnalytics, SayKitTutorialAnalytics>(Lifetime.Singleton);

            RegisterSessionServices(builder);
        }

        private static void RegisterSessionServices(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<Pool>();

            // TASK-126: UiService здесь БОЛЬШЕ НЕ регистрируется как entry-point. Прежняя строка
            // (RegisterEntryPoint<UiService>().AsSelf()) была инертна и вводила в заблуждение: Service.UI читает
            // LocalServiceContainer, а не VContainer-граф, и UiService не реализует ни одного VContainer
            // entry-point-интерфейса → его Init() отсюда не звался. UiService теперь app-level, поднимается на буте
            // (LoadingGame.InitializeLocalServices через LocalServiceContainer).
            //
            // TASK-138: но GameplayInputScreenBlocker (ниже) требует UiService КОНСТРУКТОРОМ через VContainer.
            // До TASK-138 этот скоуп никогда не инстанцировался (LoadingGame TODO), поэтому провайдер мог исчезнуть,
            // а потребитель остаться, и Build() ни разу не пытался это резолвить. Теперь прод-бут реально поднимает
            // скоуп (CreateChildFromPrefab в LoadingGame) → отсутствие регистрации бросает VContainerException и
            // уровень не собирается. Мост: регистрируем УЖЕ СУЩЕСТВУЮЩИЙ app-level инстанс (Service.UI из
            // LocalServiceContainer) как инстанс графа. Это ОДНОСТОРОННИЙ адаптер, а не второй источник правды:
            // владение остаётся у LocalServiceContainer, мы ничего не создаём заново. ADR-0003 переходится намеренно
            // (app-level UI → gameplay-VContainer), и это осознанное пересечение границы, названное здесь явно.
            // Порядок НЕСУЩИЙ: InitializeLocalServices (создаёт LocalServiceContainer+UiService) обязан идти до
            // CreateChildFromPrefab — иначе Service.UI бросит InvalidOperationException в этой строке (это его
            // штатный громкий контракт, а не тихий null). Сейчас порядок соблюдён в LoadingGame.StartAsync.
            builder.RegisterInstance(Service.UI);
            builder.RegisterEntryPoint<GameplayInputScreenBlocker>();
            // TASK-580 (built in TASK-566): one system-"back" handler for all popups — closes the top screen if it opts
            // in through IBackButtonHandler.
            builder.RegisterEntryPoint<ScreenBackButtonHandler>();

            builder.RegisterEntryPoint<TimeScale>();
            builder.RegisterEntryPoint<PauseService>();

            builder.RegisterEntryPoint<MessagesService>();
            builder.RegisterEntryPoint<RewardedStateService>();

#if SAYKIT_PURCHASING
            builder.RegisterEntryPoint<PurchaseAnalytics>();
#endif
        }
    }
}
