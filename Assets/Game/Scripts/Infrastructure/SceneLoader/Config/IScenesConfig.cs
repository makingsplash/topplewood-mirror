using System.Collections.Generic;
using UnityEngine.AddressableAssets;

namespace Common.SceneLoader.Config
{
    public interface IScenesConfig
    {
        IReadOnlyDictionary<SceneInBuild, AssetReference> SceneReferences { get; }
        bool TryGetSceneReference(SceneInBuild scene, out AssetReference sceneReference);
    }
}
