using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Common.SceneLoader
{
    public static class SceneLoaderExtensions
    {
        public static SceneInBuild GetSceneInBuild(this Scene scene) => 
            (SceneInBuild)scene.buildIndex;

        public static SceneInBuild GetActiveScene(this ISceneLoader sceneLoader)
        {
            if(sceneLoader is null)
                throw new ArgumentNullException(nameof(sceneLoader));

            return sceneLoader
                .GetActiveScene()
                .GetSceneInBuild();
        }
        
        public static bool SetActiveScene(this ISceneLoader sceneLoader, SceneInBuild sceneInBuild)
        {
            if(sceneLoader is null)
                throw new ArgumentNullException(nameof(sceneLoader));

            var scene = sceneLoader.GetScene(sceneInBuild);
            return sceneLoader.SetActiveScene(scene);
        }

        public static async UniTask<(Scene, IObjectResolver)> LoadSceneAsync<TScope>
        (
            this ISceneLoader sceneLoader,
            SceneInBuild sceneInBuild,
            LifetimeScope parentScope,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default
        )
            where TScope : LifetimeScope
        {
            using (LifetimeScope.EnqueueParent(parentScope))
            {
                var scene = await sceneLoader.LoadSceneAsync(sceneInBuild, mode: loadMode, cancellationToken: cancellationToken);
                var lifetimeScope = LifetimeScope.Find<TScope>(scene);
                return (scene, lifetimeScope.Container);
            }
        }
    }
}
