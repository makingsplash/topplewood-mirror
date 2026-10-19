using System.Collections.Generic;
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
#if UNITY_EDITOR
            // TASK-482: in the editor with no touchscreen (mouse) the co-release window widens so a pair can be
            // clicked one member after the other; device builds never compile this line, the sim never runs it.
            builder.RegisterEntryPoint<EditorMouseCoReleaseWindow>();
#endif

            // Level storage (TASK-009): authored JSON in StreamingAssets read through the ILevelStore seam,
            // replacing the hardcoded ShapeFillLevelSource (the human's 15:04 "storage system" mandate).
            builder.Register<ILevelStore, StreamingAssetsLevelStore>(Lifetime.Singleton);
            builder.Register<ILevelSource, JsonLevelSource>(Lifetime.Singleton);
            // TASK-528: the replay circle after the line-up (shapefill_pool.json, ids → line-up indices). Context-free:
            // prod and bringup both progress through CampaignProgressionController, which takes it.
            builder.Register<LevelPool>(Lifetime.Singleton);

            // ICampaignStore is bound by the hosting scope (TASK-044): prod MetaCampaignStore writes the SDK-canon
            // Data.Meta.CurrentLevel; bringup PrefsCampaignStore owns its own PlayerPrefsInt (needs only Init).
            // Kept out of this context-independent core installer, exactly like IGameplayInputSource (RFC §2.3).

            // Physics adapter: one instance seen as both the generic seam and the shape_fill contract.
            builder.Register<UnityShapeFillSimulation>(Lifetime.Singleton)
                .As<IShapeFillSimulation>()
                .As<IGameplaySimulation>();

            builder.Register<IMatchEvaluator, PoseMatchEvaluator>(Lifetime.Singleton);
            builder.Register<LevelDataValidator>(Lifetime.Singleton);
            // TASK-634: the factory files every live view by placement index, so the win beat can fly the won ones.
            builder.Register<PieceViewRegistry>(Lifetime.Singleton);
            builder.Register<IPieceViewFactory, PieceViewFactory>(Lifetime.Singleton);
            // IGameplayInputSource is bound by the hosting scope (prod: InputService-backed; bringup: raw
            // pointer) so this core installer stays context-independent (RFC §2.3).

            builder.Register<GameController>(Lifetime.Singleton);

            // TASK-566: the runner's pause gate. Shared by prod and bringup; only the prod GameplayInputScreenBlocker
            // takes it (settings popup), so bringup ticks exactly as before. The sim news GameController itself and
            // never builds the runner — poseHash is untouched by construction.
            builder.Register<GameplayPause>(Lifetime.Singleton);
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

            // Tutorial system (TASK-049, step-by-step TASK-078, beats from data TASK-613) — context-INDEPENDENT parts
            // only: the reentrant input gate consumed by the input decorator, the tour's own overlay view (TourView)
            // and its numbers, the hint's separate highlight view, the target card's pose for both, the per-level recipe
            // catalog, and the runner entry point (registered AFTER
            // CampaignProgressionController so the resumed level is already loaded into GameModel.CurrentLevel by the
            // time the runner starts — TASK-112 gates the tour on CurrentLevel.LevelId, not on the persisted progress, so the
            // ordering requirement stands and is now load-order, not pointer-resume). The context-DEPENDENT seams (progress store / gate /
            // analytics) and the input decorator itself are bound by the hosting scope — exactly like
            // IGameplayInputSource / ICampaignStore (RFC §4). Like the dev cheat, tutorial IMPLEMENTATIONS are
            // NOT registered here. The headless SimRun builds the domain with bare `new`, so it never creates
            // this runner → the sim physically cannot play the tutorial (RFC §7).
            builder.Register<ITutorialInputGate, TutorialInputGate>(Lifetime.Singleton);
            builder.RegisterInstance(new TourConfig());
            builder.Register<TargetCardLocator>(Lifetime.Singleton);
            // TASK-634: the win beat «В рамку» on EVERY win (replaced the TASK-613 one-shot WinInterlude the tour armed).
            // Both hosting flows (prod GatedResolutionFlow, bringup SequencedResolutionFlow) take IWinBeat; no card / no
            // figure → the flow keeps its WinSettleSeconds pause. The headless SimRun news the domain and never sees it.
            builder.RegisterInstance(new WinBeatConfig());
            builder.Register<IWinBeat, WinBeatView>(Lifetime.Singleton);
            // Two views, one writer each (TASK-613 closes TASK-383/244/550): the tour draws through ITourView, the Hint
            // booster through IPieceHighlightView — neither can clear the other's overlay.
            builder.Register<ITourView, TourView>(Lifetime.Singleton);
            builder.Register<IPieceHighlightView, PieceHighlightView>(Lifetime.Singleton);
            builder.RegisterInstance(BuildTutorialCatalog());
            // TASK-635: the runner also takes IHudTourTargets — bound by the HOSTING scope (prod: the Hint button;
            // bringup: none), like ICampaignStore, because the targets are prod-only presenters.
            builder.RegisterEntryPoint<TutorialRunner>();
            // NOTE: the dev level-switch cheat is NOT registered here. This core installer is context-independent
            // (shared with prod), and prod raises its own GameDebugMenu via LoadingGame — registering a cheat here
            // would spawn a SECOND menu in prod. The bringup-only DDM host is registered by
            // ShapeFillBringupLifetimeScope instead (TASK-047).
        }

        /// <summary>
        /// Per-level tutorial catalog (TASK-377, closing the TASK-078/243 hardcode): TutorialRunner builds the tour for
        /// the LOADED level's id, so different levels teach different things. Keyed by LevelId (not campaign index),
        /// robust to a dev level-jump / manifest re-order (same reason as the id-based gate). A tour is a RECIPE
        /// (TASK-613): its tap beats come from the level's certified winningOrder, so adding a tour to another level
        /// is one line here and no code. Every level listed here must carry a non-empty winningOrder in the manifest —
        /// held by TourCatalogDataTests. Public static so that test reads THIS catalog, not a copy.
        ///
        /// sailboat (campaign index 0) → the "assemble the picture" FTUE: tap hull, tap sail, then the win (the common win
        /// beat «В рамку» draws it since TASK-634; the tour's own "Совпало" beat is gone).
        /// potted_tree (campaign index 1, TASK-632) → the same recipe on the second level: tap pot, trunk, hammer (the
        /// hammer fires the catapult that throws the pre-placed crown), then the win. Its own id, so its progress mark is
        /// independent of the first tour's.
        /// diamond_gem (campaign index 3, TASK-635) → the Hint booster's tour: the button appears, a HUD beat points at it,
        /// the hint's own outline then shows winningOrder[0] (tri_tip, the core's bottom — the trap the wrong order loses
        /// on) and the tour ends on its release. The place is read back by HintUnlockController through LevelIdOf, and
        /// TourCatalogDataTests holds it inside the owner's "4–6" frame.
        /// </summary>
        public static TutorialCatalog BuildTutorialCatalog() => new(new Dictionary<string, ITourRecipe>
        {
            ["sailboat"] = new AssembleTourRecipe("first_tutorial"),
            ["potted_tree"] = new AssembleTourRecipe("second_level_tutorial"),
            ["diamond_gem"] = new BoosterTourRecipe(BoosterTourRecipe.HintTourId, BoosterTourRecipe.HintButtonTargetId),
        });
    }
}
