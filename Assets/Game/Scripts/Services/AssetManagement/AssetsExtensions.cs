using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Game.Services.AssetManagement;

namespace Game.Scripts.Services.AssetManagement
{
    public static class AssetsExtensions
    {
        public static async UniTask<TComponent> LoadAssetByComponentAsync<TComponent>(this IAssets assets, string key) where TComponent : Component
        {
            var gameObject = await assets.Load<GameObject>(key);
            var component = gameObject.GetComponent<TComponent>();
            return component;
        }
		
        public static UniTask<TComponent> LoadAssetByComponentAsync<TComponent>(this IAssets assets, AssetReference key) where TComponent : Component => 
            assets.Load<TComponent>(key.AssetGUID);
    }
}
