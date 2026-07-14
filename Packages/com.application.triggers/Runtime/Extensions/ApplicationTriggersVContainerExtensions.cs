#if VCONTAINER
using System;
using VContainer;
using VContainer.Unity;

namespace Infrastructure.ApplicationTriggers
{
    public static class ApplicationTriggersVContainerExtensions
    {
        public static IContainerBuilder RegisterApplicationTriggers(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder
                .RegisterComponentOnNewGameObject<ApplicationPauseTrigger>(lifetime, nameof(ApplicationPauseTrigger))
                .DontDestroyOnLoad()
                .AsImplementedInterfaces();
            
            builder
                .RegisterComponentOnNewGameObject<ApplicationFocusTrigger>(lifetime, nameof(ApplicationFocusTrigger))
                .DontDestroyOnLoad()
                .AsImplementedInterfaces();
            
            builder.RegisterEntryPoint<ApplicationQuitTrigger>(lifetime);
            
            builder
                .Register<ApplicationTriggers>(lifetime)
                .As<IApplicationTriggers>();
            
            return builder;
        }
    }
}
#endif
