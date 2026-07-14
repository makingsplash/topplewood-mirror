using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Tools.Addressables
{
    public static class AssetReferenceSpriteExtension
    {
        public static async UniTask<Sprite> GetSpriteAsync(this AssetReferenceSprite reference)
        {
            if (reference.IsValid())
                return reference.Asset as Sprite;

            return await reference.LoadAssetAsync().ToUniTask();
        }
    }
}
