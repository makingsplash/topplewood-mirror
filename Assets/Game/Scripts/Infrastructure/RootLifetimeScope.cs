using System.Collections.Generic;
using Common.SceneLoader.DI;
using Diagnostics;
using Infrastructure.ApplicationTriggers;
using LoadingScreen;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Game.Services.AssetManagement;
using Game.Services.LogService;

namespace Infrastructure
{
    public class RootLifetimeScope : LifetimeScope
    {
        [SerializeField] private SceneLoaderInstaller _sceneLoaderInstaller;
        [SerializeField] private LoadingScreenInstaller _loadingScreenInstaller;
        
        private readonly List<IInstaller> _installers = new()
        {
            new DiagnosticsInstaller(),
        };

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            
            _installers.ForEach(installer => installer.Install(builder));
            
            _sceneLoaderInstaller.Install(builder);
            _loadingScreenInstaller.Install(builder);
            
            RegisterEssentialServices(builder);

            builder.RegisterApplicationTriggers();
        }

        private static void RegisterEssentialServices(IContainerBuilder builder)
        {
            builder.RegisterInstance(Debug.unityLogger).As<ILogger>();
            // builder.Register<ILogService, UnityLogService>(Lifetime.Singleton);
            builder.Register<ILogService, WrapperUnityLogService>(Lifetime.Singleton);
            builder.RegisterBuildCallback(container => StaticLogService.Set(container.Resolve<ILogService>()));
            builder.Register<IAssets, AssetsProvider>(Lifetime.Singleton);
        }
    }
}
