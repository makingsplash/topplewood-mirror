using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace LoadingScreen
{
    [Serializable]
    public sealed class LoadingScreenInstaller : IInstaller
    {
        [SerializeField] private LoadingScreen _loadingScreenPrefab;
        
        public void Install(IContainerBuilder builder)
        {
            builder
                .RegisterComponentInNewPrefab(_loadingScreenPrefab, Lifetime.Singleton)
                .DontDestroyOnLoad()
                .As<ILoadingScreen>();
        }
    }
}
