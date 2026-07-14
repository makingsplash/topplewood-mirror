using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Common.SceneLoader.DI
{
    [Serializable]
    public class SceneLoaderInstaller : IInstaller
    {
        [SerializeField] SceneConfigInstaller _sceneConfigInstaller;
        
        public void Install(IContainerBuilder builder)
        {
            _sceneConfigInstaller.Install(builder);
            
            builder.RegisterEntryPoint<SceneLoader>();
        }
    }
}
