using UnityEngine;
using VContainer.Unity;

namespace VContainer
{
    public abstract class ScriptableObjectInstaller : ScriptableObject, IInstaller
    {
        public abstract void Install(IContainerBuilder builder);
    }
    
    public class ScriptableObjectInstaller<TInstaller> : ScriptableObjectInstaller, IInstaller where TInstaller : IInstaller
    {
        [SerializeField] private TInstaller _installer;
        
        public override void Install(IContainerBuilder builder) => 
            _installer.Install(builder);
    }
}