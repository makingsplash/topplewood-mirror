using VContainer;
using VContainer.Unity;

namespace Diagnostics
{
    public sealed class DiagnosticsInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder
                .Register<Diagnostics>(Lifetime.Singleton)
                .As<IDiagnostics>();
        }
    }
}
