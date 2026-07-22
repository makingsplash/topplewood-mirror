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
            builder.RegisterInstance(new TargetPreviewConfig());
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

            // Campaign progression + meta-pointer persistence (TASK-009). ICampaignStore is separate from the
            // in-level IShapeFillSaveStore; only the level number is saved (kill mid-level = level from scratch).
            builder.Register<ICampaignStore, FileCampaignStore>(Lifetime.Singleton);

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
            builder.RegisterEntryPoint<CampaignProgressionController>();
            builder.RegisterEntryPoint<ShapeFillPresenter>();
            builder.RegisterEntryPoint<TargetPreviewPresenter>();
        }
    }
}
