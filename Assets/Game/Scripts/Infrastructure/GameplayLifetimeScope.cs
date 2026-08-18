using System.Collections.Generic;
using DataStorage;
using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.DI;
using Gameplay.PlayScreen;
using Gameplay.Sequence.DI;
using Gameplay.Tutorial;
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

            // TASK-116: CampaignProgressionController now requires an ILevelResolutionFlow; prod binds the
            // immediate impl so the transition still happens the instant the level resolves — bit-for-bit the
            // pre-TASK-116 behaviour, no play screen. Required for this scope to resolve at all (a double bind of
            // the interface is disallowed, so a context-dependent single bind here is the only correct option).
            // Loading.unity is untouched and this scope is not in any build today (LoadingGame TODO) → zero runtime
            // effect on what ships; it is the behaviour-preserving DI symmetry for the seam.
            builder.Register<ILevelResolutionFlow, ImmediateResolutionFlow>(Lifetime.Singleton);

            // Play screen minimum (TASK-138): the always-on RESTART button + its confirm popup now live in the
            // PROD graph, not only in bringup. Until this task the prod scope raised neither and was never
            // instantiated at all (LoadingGame TODO) — RESTART existed solely in ShapeFillBringupLifetimeScope.
            // No duplicate source of truth: each scope owns its own bindings and prod/bringup never coexist at
            // runtime (same pattern as the twice-installed SequenceInstaller). ICampaignRestart is already bound by
            // GameplayCoreInstaller. We keep the prod ImmediateResolutionFlow (above) — so PlayScreenInteraction.IsBusy
            // stays false forever and the button is always interactable; the WIN/LOSE banner (SequencedResolutionFlow
            // + PlayScreenConfig/IPlayScreenView) is intentionally NOT part of this minimum (acceptance needs level +
            // RESTART + popup, not the banner).
            builder.Register<PlayScreenInteraction>(Lifetime.Singleton);
            builder.RegisterEntryPoint<RestartButtonPresenter>();

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

            // TASK-126: UiService здесь БОЛЬШЕ НЕ регистрируется. Эта строка (RegisterEntryPoint<UiService>().AsSelf())
            // была инертна и вводила в заблуждение: Service.UI читает LocalServiceContainer, а не VContainer-граф,
            // и UiService не реализует ни одного VContainer entry-point-интерфейса → его Init() отсюда не звался,
            // Service.UI оставался null. UiService теперь app-level, поднимается на буте
            // (LoadingGame.InitializeLocalServices через LocalServiceContainer). Прочие строки ниже —
            // такой же осиротелый latent tech-debt, но их живого потребителя пока нет (вне scope TASK-126).
            builder.RegisterEntryPoint<GameplayInputScreenBlocker>();

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
