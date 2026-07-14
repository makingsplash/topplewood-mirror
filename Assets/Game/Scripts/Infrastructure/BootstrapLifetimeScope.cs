using Infrastructure.Loading;
using VContainer;
using VContainer.Unity;

namespace Game.Code.Infrastructure
{
    public class BootstrapLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            
            builder.RegisterEntryPoint<LoadingGame>();
        }
    }
}
