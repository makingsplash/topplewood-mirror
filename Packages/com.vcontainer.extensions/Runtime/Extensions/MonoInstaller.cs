using UnityEngine;
using VContainer.Unity;

namespace VContainer
{
    public abstract class MonoInstaller : MonoBehaviour, IInstaller
    {
        public abstract void Install(IContainerBuilder builder);
    }
    
    public class MonoInstaller<TInstaller> : MonoInstaller, IInstaller where TInstaller : IInstaller
    {
        [SerializeField] private TInstaller _installer;
        
        public override void Install(IContainerBuilder builder) => 
            _installer.Install(builder);
    }
}