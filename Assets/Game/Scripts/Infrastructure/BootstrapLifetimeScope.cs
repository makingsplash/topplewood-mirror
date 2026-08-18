using Infrastructure.Loading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Code.Infrastructure
{
    public class BootstrapLifetimeScope : LifetimeScope
    {
        // TASK-138: the gameplay-session scope prefab that LoadingGame raises as a child inside the loaded Loading
        // scene (ADR-0004, Variant B). Serialized here because a prefab reference can only live on a MonoBehaviour
        // scope; LoadingGame is a plain IAsyncStartable, so we RegisterInstance it and inject it into its ctor.
        [SerializeField] private GameplayLifetimeScope _gameplayScopePrefab;

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterInstance(_gameplayScopePrefab);
            builder.RegisterEntryPoint<LoadingGame>();
        }
    }
}
