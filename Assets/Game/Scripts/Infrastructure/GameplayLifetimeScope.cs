using System.Collections.Generic;
using DataStorage;
using Gameplay.Sequence.DI;
using Services;
using Services.Gameplay;
using SGS.Services.Date_Time;
using SGS.Services.Gameplay;
using SGS.Services.Pools;
using SGS.Services.Ui;
using VContainer;
using VContainer.Unity;
using Game.Infrastructure;
using Game.Infrastructure.Saving;
using Game.Services.Input;
#if SAYKIT_PURCHASING
using SGS.Store.Purchases;
#endif

namespace Game.Code.Infrastructure
{
    /// <summary>
    /// Per-session (gameplay) DI scope. TileSort gameplay registrations were removed;
    /// this is the skeleton the new game hangs its GameModel / controllers / state machine on.
    /// </summary>
    public class GameplayLifetimeScope : LifetimeScope
    {
        private readonly List<IInstaller> _installers = new()
        {
            new SequenceInstaller(),
        };

        protected override void Configure(IContainerBuilder builder)
        {
            foreach (var installer in _installers)
                installer.Install(builder);

            builder.Register<IInputService, InputService>(Lifetime.Singleton);
            builder.Register<IInputBlockersHandler, InputBlockersHandler>(Lifetime.Singleton);

            builder.RegisterInstance(Data.Configs.Core).AsSelf();

            // Saving
            builder.RegisterEntryPoint<SaveManager>();

            RegisterSessionServices(builder);
        }

        private static void RegisterSessionServices(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<Pool>();

            builder.RegisterEntryPoint<UiService>().AsSelf();
            builder.RegisterEntryPoint<GameplayInputScreenBlocker>();

            builder.RegisterEntryPoint<TimeScale>();
            builder.RegisterEntryPoint<PauseService>();

            builder.RegisterEntryPoint<MessagesService>();
            builder.RegisterEntryPoint<RewardedStateService>();

#if SAYKIT_PURCHASING
            builder.RegisterEntryPoint<PurchaseAnalytics>();
#endif
        }
    }
}
