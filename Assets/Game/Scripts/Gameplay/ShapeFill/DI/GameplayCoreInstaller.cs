using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Core.Mechanic;
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
            builder.RegisterInstance<IGameplayClock>(new GameplayClock(tuning.FixedStep));
            builder.RegisterInstance<IGameplayRandom>(new DeterministicRandom(0));
            builder.RegisterInstance<IEmbeddedLevels>(new EmbeddedTestLevels());

            builder.Register<GameModel>(Lifetime.Singleton);

            // Mechanic seams (replace Null/Empty stubs).
            builder.Register<IMechanicController, ShapeFillMechanicController>(Lifetime.Singleton);
            builder.Register<ILevelSource, ShapeFillLevelSource>(Lifetime.Singleton);

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
            builder.RegisterEntryPoint<ShapeFillPresenter>();
        }
    }
}
