using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Common.SceneLoader.Config
{
    [CreateAssetMenu(menuName = "Create ScenesConfig", fileName = "ScenesConfig", order = 0)]
    public class ScenesConfig : ScriptableObject, IScenesConfig, ISerializationCallbackReceiver
    {
        [Serializable]
        public struct SceneData
        {
            public SceneInBuild Key;
            public AssetReference SceneReference;
        }
        
        [SerializeField] private List<SceneData> _sceneDatas;
        
        private readonly Dictionary<SceneInBuild, AssetReference> _sceneReferences = new();
        
        public IReadOnlyDictionary<SceneInBuild, AssetReference> SceneReferences => _sceneReferences;
        
        public bool TryGetSceneReference(SceneInBuild scene, out AssetReference sceneReference) => 
            _sceneReferences.TryGetValue(scene, out sceneReference);

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _sceneReferences.Clear();
            foreach (var sceneData in _sceneDatas)
                _sceneReferences.Add(sceneData.Key, sceneData.SceneReference);
        }

        [ContextMenu(nameof(Validate))]
        private void Validate()
        {
            foreach (var sceneReferencesValue in _sceneReferences.Values) 
                Debug.unityLogger.Log(LogType.Log, sceneReferencesValue.ToString());
        }
    }
}
