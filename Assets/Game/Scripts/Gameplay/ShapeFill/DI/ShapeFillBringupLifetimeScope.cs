using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.DI;
using Infrastructure.ApplicationTriggers;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Self-contained root scope for the isolated gray-box bringup scene (RFC §2.3/§7): raises the shape_fill
/// core graph with a raw pointer input source and a self-contained file save-store — deliberately WITHOUT
/// Root / InputService / addressables / SayData+PlayerPrefs (RFC R5). Play the <c>ShapeFillBringup</c> scene
/// directly and the entry points spawn the level. Global namespace and class name are fixed by the scene's
/// MonoScript GUID reference — do not rename.
/// </summary>
public class ShapeFillBringupLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Core shape_fill graph: GameModel, controllers, simulation, coverage, views + the
        // GameControllerRunner / ShapeFillPresenter entry points (RFC §7).
        new GameplayCoreInstaller().Install(builder);

        // Bringup input (RFC §2.3): raw new-Input-System pointer, no IInputService/Data. One singleton is
        // both the IGameplayInputSource seam and the ticked entry point (PointerTapInputSource : ITickable).
        builder.RegisterEntryPoint<PointerTapInputSource>(Lifetime.Singleton).As<IGameplayInputSource>();

        // App lifecycle triggers (Data-free) so autosave can hook pause/focus/quit.
        builder.RegisterApplicationTriggers();

        // F2 save round-trip (RFC §4.1): file sink + domain facade. GameController.Start restores on load;
        // BringupAutosave is the Data-free save trigger for the isolated scene (production uses SaveManager).
        builder.Register<IShapeFillSaveStore, FileShapeFillSaveStore>(Lifetime.Singleton);
        builder.Register<IProgressSaver, ShapeFillProgressSaver>(Lifetime.Singleton);
        builder.RegisterEntryPoint<BringupAutosave>();
    }
}
