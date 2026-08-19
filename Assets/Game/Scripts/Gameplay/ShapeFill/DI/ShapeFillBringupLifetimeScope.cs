using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.DI;
using Gameplay.PlayScreen;
using Gameplay.Sequence.DI;
using Gameplay.Tutorial;
using Infrastructure.ApplicationTriggers;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Self-contained root scope for the isolated gray-box bringup scene (RFC §2.3/§7): raises the shape_fill
/// core graph with a raw pointer input source and a Data-free PlayerPrefs campaign store — deliberately WITHOUT
/// Root / InputService / addressables / SayData (RFC R5), but it Inits the Say prefs wrapper itself (TASK-044
/// §3) so progress persists. Play the <c>ShapeFillBringup</c> scene
/// directly and the entry points spawn the level. Global namespace and class name are fixed by the scene's
/// MonoScript GUID reference — do not rename.
/// </summary>
public class ShapeFillBringupLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Display cadence (TASK-069): prod sets Application.targetFrameRate in LoadingGame.InitializeGameSettings,
        // but the bringup scene bypasses that bootstrap, so an APK built off this scene ran at Unity's mobile
        // default (30). Owner asked for 90 explicitly (device measure showed missedFrames=0 at 30 — headroom, not
        // a perf wall). A screen/mode that can't sustain 90Hz is clamped by Unity itself; we do NOT fall back to 60.
        Application.targetFrameRate = 90;

        // Say-wrapper prefs backend (TASK-044 §3): the bringup scene omits the LoadingGame bootstrap, so it must
        // Init the collection itself before any PlayerPrefs value is constructed (PlayerPrefsBase ctor registers
        // into the singleton → NRE without Init). Idempotent guard: a second Init in the same editor session
        // (prod boot → then enter bringup) would rebuild the collection with an empty _allPrefs and orphan the
        // already-created prefs, silently breaking DeleteAllPrefs/RefreshAll. Real backend, shared with prod, so
        // progress survives Stop→Play.
        if (PlayerPrefsCollection.Instance == null)
            PlayerPrefsCollection.Init(new PrefsStorageBackend());

        // Core shape_fill graph: GameModel, controllers, simulation, coverage, views + the
        // GameControllerRunner / ShapeFillPresenter entry points (RFC §7).
        new GameplayCoreInstaller().Install(builder);

        // Bringup input (RFC §2.3): raw new-Input-System pointer, no IInputService/Data. The concrete
        // PointerTapInputSource stays the ticked entry point (ITickable) AND is resolvable by concrete type
        // (.AsSelf()); IGameplayInputSource binds to the tutorial input decorator that wraps it (TASK-049,
        // human 15:21 DI note). Splitting the two registrations avoids binding one interface twice — the old
        // `.As<IGameplayInputSource>()` on the concrete would have collided with the decorator and silently
        // broken either input or ticking.
        builder.RegisterEntryPoint<PointerTapInputSource>(Lifetime.Singleton).AsSelf();
        builder.Register<IGameplayInputSource>(
            resolver => new TutorialGatedInputSource(
                resolver.Resolve<PointerTapInputSource>(),
                resolver.Resolve<ITutorialInputGate>()),
            Lifetime.Singleton);

        // App lifecycle triggers (Data-free) so the prefs flush can hook pause/focus/quit.
        builder.RegisterApplicationTriggers();

        // Persistence (TASK-044): campaign pointer via a Data-free PrefsCampaignStore (its own PlayerPrefsInt on
        // the shared Meta.CurrentLevel key), plus the Data-free lifecycle prefs flush (bringup can't use the
        // Data.Debug-gated SaveManager). No in-level snapshot — kill mid-level reopens the level from scratch.
        builder.Register<ICampaignStore, PrefsCampaignStore>(Lifetime.Singleton);
        builder.RegisterEntryPoint<BringupPrefsFlush>();

        // Tutorial system bringup impls (TASK-049): own Data-free PlayerPrefs completion list, gate defaults on
        // (no Data.Debug here, RFC R5), no-op analytics (no GlobalService/SayKit). Context-independent parts
        // (input gate, view stub, definition, runner) come from GameplayCoreInstaller. This lets the bringup
        // scene prove the whole mechanism live on parallel impls (RFC §9).
        builder.Register<ITutorialProgressStore, PrefsTutorialProgressStore>(Lifetime.Singleton);
        builder.Register<ITutorialGate, BringupTutorialGate>(Lifetime.Singleton);
        builder.Register<ITutorialAnalytics, NullTutorialAnalytics>(Lifetime.Singleton);

        // Play screen (TASK-116): the sequence layer is context-dependent — prod raises SequenceInstaller in
        // GameplayLifetimeScope, so bringup (the only path built into the APK) must raise its own copy or
        // ISequenceFactory is unresolvable. On top of it: the bringup (sequenced) ILevelResolutionFlow, the
        // procedural WIN/LOSE banner view, its config, the busy coordinator, and the always-on Restart button.
        // The campaign controller now drives its transition through ILevelResolutionFlow (prod's GatedResolutionFlow
        // gates BOTH a loss and a win with a screen, TASK-157; here it plays banner + 2s hold first). Registered before
        // the dev DDM host: EventSystem ownership is order-independent by construction (idempotent guard in
        // RestartButtonPresenter + BringupDebugMenuHost), so this order is convenience, not a load-bearing invariant.
        new SequenceInstaller().Install(builder);
        builder.RegisterInstance(new PlayScreenConfig());
        builder.Register<PlayScreenInteraction>(Lifetime.Singleton);
        builder.Register<IPlayScreenView, PlayScreenView>(Lifetime.Singleton);
        builder.Register<ILevelResolutionFlow, SequencedResolutionFlow>(Lifetime.Singleton);
        builder.RegisterEntryPoint<RestartButtonPresenter>();

#if GPIE_DEVELOPMENT
        // Dev/QA level-switch cheat (TASK-047): the reachable SayKit DynamicDebugMenu UI, hosted inside this
        // bringup scope so it can inject CampaignProgressionController by ordinary DI (replaces the removed IMGUI
        // overlay). Registered HERE, not in the context-independent GameplayCoreInstaller, because prod raises its
        // own GameDebugMenu via LoadingGame — a core-installer registration would spawn a SECOND menu in prod.
        builder.RegisterEntryPoint<Gameplay.Dev.BringupDebugMenuHost>();

        // Per-fixed-step pose logger (TASK-070): a read-only diagnostic that attaches itself as the
        // GameController.StepObserver and emits one [STEPLOG] line per physics step, so the same dev build
        // run in the Editor and on device can be diffed to pin where cross-device physics diverges (ADR-0001
        // M0.2). Never touches the simulation/model → physics and poseHash are unchanged. Bringup-only, like
        // the DDM host: prod raises its own graph via LoadingGame, and the headless sim news the domain
        // directly so it never creates this entry point.
        builder.RegisterEntryPoint<Gameplay.Dev.StepPoseLogger>();
#endif
    }
}
