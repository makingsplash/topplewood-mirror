using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
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
#if GPIE_DEVELOPMENT
            // .AsSelf() also makes the same singleton resolvable by concrete type so the dev level-switch cheat
            // (TASK-011) can inject it and drive DevJumpToLevel. Player builds keep interface-only registration.
            builder.RegisterEntryPoint<CampaignProgressionController>().AsSelf();
#else
            builder.RegisterEntryPoint<CampaignProgressionController>();
#endif
            // Frames Camera.main to the level content (TASK-009, RFC 07 §1) so tall levels (L5) fit on screen
            // and every piece is tappable. Registered BEFORE TargetPreviewPresenter, which anchors to the
            // camera viewport — the camera must be framed first.
            builder.RegisterEntryPoint<CameraFramingPresenter>();
            // Renders opt-in VISIBLE static geometry (TASK-031c); no-op for levels without visuals.
            builder.RegisterEntryPoint<StaticGeometryPresenter>();
            builder.RegisterEntryPoint<ShapeFillPresenter>();
            builder.RegisterEntryPoint<TargetPreviewPresenter>();

            // Tutorial system (TASK-049, step-by-step TASK-078) — context-INDEPENDENT parts only: the reentrant
            // input gate consumed by the input decorator, the real ring/finger overlay view (TutorialView), the
            // tour definition + its composite AssembleInOrderStep, and the runner entry point (registered AFTER
            // CampaignProgressionController so ReadLevelIndex is already
            // resumed and the resumed level is loaded). The context-DEPENDENT seams (progress store / gate /
            // analytics) and the input decorator itself are bound by the hosting scope — exactly like
            // IGameplayInputSource / ICampaignStore (RFC §4). Like the dev cheat, tutorial IMPLEMENTATIONS are
            // NOT registered here. The headless SimRun builds the domain with bare `new`, so it never creates
            // this runner → the sim physically cannot play the tutorial (RFC §7).
            builder.Register<ITutorialInputGate, TutorialInputGate>(Lifetime.Singleton);
            builder.Register<ITutorialView, TutorialView>(Lifetime.Singleton);
            // TEMPORARY INPUT (TASK-078, owner directive 1): the giraffe tutorial level's winning tap order is
            // hardcoded here = placement-order 0..6 (leg, leg, body, neck, head, horn_a, horn_b, bottom-up; re-verified
            // after TASK-084 split the single horn into horn_a/horn_b by sweep level-sim_20260804_170440.json:
            // slot-ascending is the sole reliable solve order and equals placement order 0..6, so the constant is
            // unchanged). AssembleInOrderStep itself is level-agnostic — remove this
            // array once a certified order is exported into LevelData and fed from there (TASK-078 tech-debt).
            var giraffeTapOrder = new[] { 0, 1, 2, 3, 4, 5, 6 };
            builder.RegisterInstance(new TutorialDefinition(
                "first_tutorial", new ITutorialStep[] { new AssembleInOrderStep(giraffeTapOrder) }));
            builder.RegisterEntryPoint<TutorialRunner>();
            // NOTE: the dev level-switch cheat is NOT registered here. This core installer is context-independent
            // (shared with prod), and prod raises its own GameDebugMenu via LoadingGame — registering a cheat here
            // would spawn a SECOND menu in prod. The bringup-only DDM host is registered by
            // ShapeFillBringupLifetimeScope instead (TASK-047).
        }
    }
}
