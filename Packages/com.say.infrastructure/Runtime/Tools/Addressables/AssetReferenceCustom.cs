using System;

using Cysharp.Threading.Tasks;

using UnityEditor;

using UnityEngine;
using UnityEngine.AddressableAssets;

namespace SGS.Infrastructure.Tools.Addressables
{
    /// <summary>
    /// Canvas only asset reference.
    /// </summary>
    [Serializable]
    public abstract class AssetReferenceCustom<T> : AssetReferenceT<T> where T : Component
    {

        /// <summary>
        /// Constructs a new reference to a Canvas.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceCustom(string guid) : base(guid)
        {
        }

        public async UniTask<T> InstantiateAsync(IProgress<float> progress = null)
        {
            var task = base.InstantiateAsync().ToUniTask(progress);
            GameObject gameObject = await task;
            return gameObject.GetComponent<T>();
        }
        
        public async UniTask<T> InstantiateAsync(bool InstatiateInWorldSpace, Transform parent)
        {
            GameObject gameObject = await base.InstantiateAsync(parent, InstatiateInWorldSpace);
            return gameObject.GetComponent<T>();
        }
        
        public async UniTask<T> InstantiateAsync(Transform parent, Vector3 position, Quaternion rotation)
        {
            GameObject gameObject = await base.InstantiateAsync(position, rotation, parent);
            return gameObject.GetComponent<T>();
        }
        
        public T Instantiate()
        {
            return base.InstantiateAsync().WaitForCompletion().GetComponent<T>();
        }

        public override bool ValidateAsset(string path)
        {
#if UNITY_EDITOR
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (typeof(GameObject).IsAssignableFrom(type))
            {
                GameObject gameObject = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
                return gameObject != null
                    ? (AssetDatabase.LoadMainAssetAtPath(path) as GameObject).TryGetComponent(typeof(T), out _)
                    : false;
            }

            return false;
#else
            return false;
#endif
        }
    }
}