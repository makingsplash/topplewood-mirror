using Common.SceneLoader.Config;
using UnityEngine;
using VContainer;

namespace Common.SceneLoader.DI
{
    [CreateAssetMenu(menuName = "Create SceneConfigInstaller", fileName = "SceneConfigInstaller", order = 0)]
    public class SceneConfigInstaller : ScriptableObjectInstaller
    {
        [SerializeField] private ScenesConfig _scenesConfig;
        
        public override void Install(IContainerBuilder builder)
        {
            builder
                .RegisterInstance(_scenesConfig)
                .As<IScenesConfig>();
        }
    }
}
