using System;
using System.Threading;
using Common.SceneLoader.Config;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Common.SceneLoader
{
    public sealed class SceneLoader : ISceneLoader
    {
        private readonly IScenesConfig _scenesConfig;

        public SceneLoader(IScenesConfig scenesConfig)
        {
            _scenesConfig = scenesConfig ?? throw new ArgumentNullException(nameof(scenesConfig));
        }

        public Scene GetActiveScene() => 
            SceneManager.GetActiveScene();

        public Scene GetScene(SceneInBuild scene) => 
            SceneManager.GetSceneByBuildIndex((int)scene);

        public bool SetActiveScene(Scene scene) => 
            SceneManager.SetActiveScene(scene);

        public async UniTask<Scene> LoadSceneAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default)
        {
            var asyncOperation = SceneManager.LoadSceneAsync((int)scene, mode);
            if (asyncOperation is null)
                throw new InvalidOperationException(
                    $"SceneManager.LoadSceneAsync returned null for {scene} (index {(int)scene}). " +
                    "The scene may already be loading, the build index may be invalid, or the app is quitting.");

            await asyncOperation.ToUniTask(progress, cancellationToken: cancellationToken);

            return SceneManager.GetSceneByBuildIndex((int)scene);
        }
        
        public async UniTask<Scene> LoadSceneByAddressableAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default)
        {
            _scenesConfig.TryGetSceneReference(scene, out var sceneReference);

            var handle = Addressables.LoadSceneAsync(sceneReference, mode);

            // handle.ToUniTask polls cancellation only via PlayerLoopTiming.Update.
            // On Android, Addressables may stall the player loop (CDN download,
            // catalog check, shader compilation) so the poll never runs.
            // Register a thread-pool callback to release the handle immediately
            // when the token fires — this stops the underlying operation regardless
            // of whether the player loop is ticking.
            using var reg = cancellationToken.Register(
                () => { if (!handle.IsDone) Addressables.Release(handle); },
                useSynchronizationContext: false);   // fire on thread-pool, not main thread

            await handle.ToUniTask(progress, cancellationToken: cancellationToken);

            return handle.Result.Scene;
        }

        public void MergeScenes(Scene source, Scene destination) => 
            SceneManager.MergeScenes(source, destination);

        public UniTask UnloadSceneAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            UnloadSceneOptions options = UnloadSceneOptions.None,
            CancellationToken cancellationToken = default)
        {
            var asyncOperation = SceneManager.UnloadSceneAsync((int)scene, options);
            return asyncOperation?.ToUniTask(progress, cancellationToken: cancellationToken) ?? UniTask.CompletedTask;
        }

        public UniTask UnloadSceneAsync(
            Scene scene,
            IProgress<float> progress = null,
            UnloadSceneOptions options = UnloadSceneOptions.None,
            CancellationToken cancellationToken = default)
        {
            var asyncOperation = SceneManager.UnloadSceneAsync(scene, options);
            return asyncOperation?.ToUniTask(progress, cancellationToken: cancellationToken) ?? UniTask.CompletedTask;
        }
    }
}
