using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
using Gameplay.PlayScreen;
using Gameplay.Tutorial;
using Gameplay.View;
using VContainer;
using VContainer.Unity;

namespace Gameplay.DI
{
    /// <summary>
    /// Per-session gameplay-core registrations (RFC §7). Added by one line to
    /// <c>GameplayLifetimeScope._installers</c>. Swaps the mechanic-agnostic stubs for the shape_fill
    /// implementations and wires the domain + adapters.
    /// </summary>
    public sealed class GameplayCoreInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            var tuning = new ShapeFillTuning();
            builder.RegisterInstance(tuning);
            builder.RegisterInstance(new TargetPreviewConfig());
            builder.RegisterInstance(new CameraFramingConfig());
            // TASK-085: the min tap zone config + its runtime adapter (live Camera/Screen → world halo radius). The
            // adapter reads Camera.main like CameraFramingPresenter (below), so it belongs in this same installer; the
            // headless sim never runs this — SimRun bare-news the domain with NullTapTolerance instead.
            builder.RegisterInstance(new TapZoneConfig());
            builder.Register<IGameplayTapTolerance, CameraTapTolerance>(Lifetime.Singleton);
            builder.RegisterInstance<IGameplayClock>(new GameplayClock(tuning.FixedStep));
            builder.RegisterInstance<IGameplayRandom>(new DeterministicRandom(0));
            builder.RegisterInstance<IEmbeddedLevels>(new EmbeddedTestLevels());

            builder.Register<GameModel>(Lifetime.Singleton);

            // Mechanic seams (replace Null/Empty stubs).
            builder.Register<IMechanicController, ShapeFillMechanicController>(Lifetime.Singleton);

            // Level storage (TASK-009): authored JSON in StreamingAssets read through the ILevelStore seam,
            // replacing the hardcoded ShapeFillLevelSource (the human's 15:04 "storage system" mandate).
            builder.Register<ILevelStore, StreamingAssetsLevelStore>(Lifetime.Singleton);
            builder.Register<ILevelSource, JsonLevelSource>(Lifetime.Singleton);

            // ICampaignStore is bound by the hosting scope (TASK-044): prod MetaCampaignStore writes the SDK-canon
            // Data.Meta.CurrentLevel; bringup PrefsCampaignStore owns its own PlayerPrefsInt (needs only Init).
            // Kept out of this context-independent core installer, exactly like IGameplayInputSource (RFC §2.3).

            // Physics adapter: one instance seen as both the generic seam and the shape_fill contract.
            builder.Register<UnityShapeFillSimulation>(Lifetime.Singleton)
                .As<IShapeFillSimulation>()
                .As<IGameplaySimulation>();

            builder.Register<IMatchEvaluator, PoseMatchEvaluator>(Lifetime.Singleton);
            builder.Register<LevelDataValidator>(Lifetime.Singleton);
            builder.Register<IPieceViewFactory, PieceViewFactory>(Lifetime.Singleton);
            // IGameplayInputSource is bound by the hosting scope (prod: InputService-backed; bringup: raw
            // pointer) so this core installer stays context-independent (RFC §2.3).

            builder.Register<GameController>(Lifetime.Singleton);

            builder.RegisterEntryPoint<GameControllerRunner>();
            // After the runner loads level 0, this resumes the persisted campaign index and drives WIN→next /
            // LOSS→retry reloads (TASK-009). Registered before the presenters so they render the resumed level.
            // .As<ICampaignRestart>() exposes the play-screen restart seam (TASK-116) — the same singleton, so
            // the Restart button reloads through the existing index owner. (RegisterEntryPoint's
            // AsImplementedInterfaces already covers it; kept explicit at the registration site for intent.)
            // ILevelResolutionFlow is bound by the hosting scope, not here (context-dependent, like ICampaignStore).
#if GPIE_DEVELOPMENT
            // .AsSelf() also makes the same singleton resolvable by concrete type so the dev level-switch cheat
            // (TASK-011) can inject it and drive DevJumpToLevel. Player builds keep interface-only registration.
            builder.RegisterEntryPoint<CampaignProgressionController>().AsSelf().As<ICampaignRestart>();
#else
            builder.RegisterEntryPoint<CampaignProgressionController>().As<ICampaignRestart>();
#endif
            // Frames Camera.main to the level content (TASK-009, RFC 07 §1) so tall levels (L5) fit on screen
            // and every piece is tappable. Registered BEFORE TargetPreviewPresenter, which anchors to the
            // camera viewport — the camera must be framed first.
            builder.RegisterEntryPoint<CameraFramingPresenter>();
            // Renders opt-in VISIBLE static geometry (TASK-031c); no-op for levels without visuals.
            builder.RegisterEntryPoint<StaticGeometryPresenter>();
            // Renders directed air-flow zones (TASK-097); no-op for levels without AirZones. The headless SimRun
            // bare-news the domain without this installer, so the sim sees flow only through the adapter (no view).
            builder.RegisterEntryPoint<AirZonePresenter>();
            builder.RegisterEntryPoint<ShapeFillPresenter>();
            builder.RegisterEntryPoint<TargetPreviewPresenter>();

            // Tutorial system (TASK-049, step-by-step TASK-078) — context-INDEPENDENT parts only: the reentrant
            // input gate consumed by the input decorator, the real ring/finger overlay view (TutorialView), the
            // tour definition + its composite AssembleInOrderStep, and the runner entry point (registered AFTER
            // CampaignProgressionController so the resumed level is already loaded into GameModel.CurrentLevel by the
            // time the runner starts — TASK-112 gates the tour on CurrentLevel.LevelId, not on ReadLevelIndex, so the
            // ordering requirement stands and is now load-order, not pointer-resume). The context-DEPENDENT seams (progress store / gate /
            // analytics) and the input decorator itself are bound by the hosting scope — exactly like
            // IGameplayInputSource / ICampaignStore (RFC §4). Like the dev cheat, tutorial IMPLEMENTATIONS are
            // NOT registered here. The headless SimRun builds the domain with bare `new`, so it never creates
            // this runner → the sim physically cannot play the tutorial (RFC §7).
            builder.Register<ITutorialInputGate, TutorialInputGate>(Lifetime.Singleton);
            builder.Register<ITutorialView, TutorialView>(Lifetime.Singleton);
            // TEMPORARY INPUT (TASK-078, owner directive 1): the FIRST campaign level's winning tap order is
            // hardcoded here. TASK-100 made sailboat campaign index 0 (was giraffe), so this is now sailboat's
            // 2-piece order = placement-order 0..1 (hull, then sail; bottom-up). For a 2-piece stack the ONLY solve
            // order is hull-before-sail (sail has no base to rest on otherwise) = slot-ascending = placement order
            // 0..1 — sailboat is the canonical k=2 example in level-authoring.md §L13. sailboat's per-level
            // winnability is unchanged by TASK-100 (its LevelData is byte-identical to the shipped corpus; only its
            // array index moved), so the winning order is inherited, not newly introduced.
            // AssembleInOrderStep itself is level-agnostic — remove this array once a certified order is exported
            // into LevelData and fed from there (TASK-078 tech-debt; still dormant because winning order ==
            // placement order here, so TASK-079's "winning order ≠ placement" trigger has NOT fired).
            var sailboatTapOrder = new[] { 0, 1 };
            builder.RegisterInstance(new TutorialDefinition(
                "first_tutorial", new ITutorialStep[] { new AssembleInOrderStep(sailboatTapOrder) }));
            builder.RegisterEntryPoint<TutorialRunner>();
            // NOTE: the dev level-switch cheat is NOT registered here. This core installer is context-independent
            // (shared with prod), and prod raises its own GameDebugMenu via LoadingGame — registering a cheat here
            // would spawn a SECOND menu in prod. The bringup-only DDM host is registered by
            // ShapeFillBringupLifetimeScope instead (TASK-047).
        }
    }
}
