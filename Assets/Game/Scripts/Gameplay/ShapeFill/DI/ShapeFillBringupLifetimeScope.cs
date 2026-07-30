using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.DI;
using Infrastructure.ApplicationTriggers;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
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

        // Bringup input (RFC §2.3): raw new-Input-System pointer, no IInputService/Data. One singleton is
        // both the IGameplayInputSource seam and the ticked entry point (PointerTapInputSource : ITickable).
        builder.RegisterEntryPoint<PointerTapInputSource>(Lifetime.Singleton).As<IGameplayInputSource>();

        // App lifecycle triggers (Data-free) so the prefs flush can hook pause/focus/quit.
        builder.RegisterApplicationTriggers();

        // Persistence (TASK-044): campaign pointer via a Data-free PrefsCampaignStore (its own PlayerPrefsInt on
        // the shared Meta.CurrentLevel key), plus the Data-free lifecycle prefs flush (bringup can't use the
        // Data.Debug-gated SaveManager). No in-level snapshot — kill mid-level reopens the level from scratch.
        builder.Register<ICampaignStore, PrefsCampaignStore>(Lifetime.Singleton);
        builder.RegisterEntryPoint<BringupPrefsFlush>();
    }
}
