using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Common.SceneLoader
{
    public interface ISceneLoader
    {
        Scene GetActiveScene();
        Scene GetScene(SceneInBuild scene);

        bool SetActiveScene(Scene scene);

        UniTask<Scene> LoadSceneAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default);

        void MergeScenes(Scene source, Scene destination);

        UniTask UnloadSceneAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            UnloadSceneOptions options = UnloadSceneOptions.None,
            CancellationToken cancellationToken = default);

        UniTask UnloadSceneAsync(
            Scene scene,
            IProgress<float> progress = null,
            UnloadSceneOptions options = UnloadSceneOptions.None,
            CancellationToken cancellationToken = default);

        UniTask<Scene> LoadSceneByAddressableAsync(
            SceneInBuild scene,
            IProgress<float> progress = null,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default);
    }
}
