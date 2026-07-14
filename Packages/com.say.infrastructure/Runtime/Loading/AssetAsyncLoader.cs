using System;
using System.Reflection;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

using Object = UnityEngine.Object;


namespace SGS.Infrastructure.Loading
{
    public interface IAssetAsyncLoader
    {
        float PercentComplete { get; }
        void StartLoading(Action completeAction);
    }
    
    public class AssetAsyncLoader<T> : IAssetAsyncLoader where T : Object
    {
        private readonly string _assetPath;
        private readonly Action<T> _assignAction;
        private Action _completeAction;
        
        private AsyncOperationHandle<T> _loadingRequest;

        public float PercentComplete => _loadingRequest.PercentComplete;

        public AssetAsyncLoader(string assetPath, Action<T> assignAction)
        {
            _assetPath = assetPath;
            _assignAction = assignAction;
        }

        // Load asset with GameConfigAttribute
        public AssetAsyncLoader(Action<T> assignAction)
        {
            _assetPath = GameConfigAttribute.GetPathFromAttribute<T>();
            _assignAction = assignAction;
        }

        public void StartLoading(Action completeAction)
        {
            _completeAction = completeAction;

            _loadingRequest = Addressables.LoadAssetAsync<T>(_assetPath);
            
            _loadingRequest.Completed += OnEndLoading;
        }

        private void OnEndLoading(AsyncOperationHandle<T> obj)
        {
            _loadingRequest.Completed -= OnEndLoading;

            if (_loadingRequest.Status == AsyncOperationStatus.Failed || 
                _loadingRequest.Status == AsyncOperationStatus.None || 
                _loadingRequest.Result == null)
            {
                Debug.LogError("Asset not load: " + _assetPath);
            }
            
            _assignAction.Invoke(_loadingRequest.Result);
            _completeAction.Invoke();
        }
    }
}