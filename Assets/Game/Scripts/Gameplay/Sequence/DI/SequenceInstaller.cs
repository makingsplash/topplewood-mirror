using VContainer;
using VContainer.Unity;

namespace Gameplay.Sequence.DI
{
    public sealed class SequenceInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            RegisterSequenceFactory(builder);
            RegisterSequences(builder);
        }

        private static void RegisterSequenceFactory(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<SequenceFactory>();
        }

        // Register concrete game sequences here as the new game defines them, e.g.:
        //     builder.Register<MyInitGameSequence>(Lifetime.Singleton);
        private static void RegisterSequences(IContainerBuilder builder)
        {
        }
    }
}
