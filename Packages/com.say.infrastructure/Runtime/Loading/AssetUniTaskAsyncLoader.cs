using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace SGS.Infrastructure.Loading
{
    public interface IAssetUniTaskAsyncLoader
    {
        float PercentComplete { get; }
        UniTask Load();
    }
    
    public class AssetUniTaskAsyncLoader<T> : IAssetUniTaskAsyncLoader where T : Object
    {
        private readonly string _assetPath;
        private readonly Action<T> _assignAction;

        private AsyncOperationHandle<T> _loadingRequest;

        public float PercentComplete => _loadingRequest.PercentComplete;

        public AssetUniTaskAsyncLoader(string assetPath, Action<T> assignAction)
        {
            _assetPath = assetPath;
            _assignAction = assignAction;
        }

        // Load asset with GameConfigAttribute
        public AssetUniTaskAsyncLoader(Action<T> assignAction)
        {
            var configAttribute = typeof(T).GetCustomAttribute<GameConfigAttribute>();
            _assetPath = configAttribute.Path;
            _assignAction = assignAction;
        }
        
        public async UniTask Load()
        {
            _loadingRequest = Addressables.LoadAssetAsync<T>(_assetPath);

            await _loadingRequest;
            
            if (_loadingRequest.Status == AsyncOperationStatus.Failed || 
                _loadingRequest.Status == AsyncOperationStatus.None || 
                _loadingRequest.Result == null)
            {
                Debug.LogError("Asset not load: " + _assetPath);
            }
            
            _assignAction.Invoke(_loadingRequest.Result);
        }
    }
}