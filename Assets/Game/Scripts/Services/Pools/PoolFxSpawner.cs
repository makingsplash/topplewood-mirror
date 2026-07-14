using Cysharp.Threading.Tasks;
using SGS.Services.Pools;
using UnityEngine;

namespace Services.Pools
{
    /// <summary>
    /// Спавн FX из пула со скиновым переопределением: если ссылка из скина не задана,
    /// используется базовый ключ пула (константа из <see cref="PoolItems"/>).
    /// </summary>
    public static class PoolFxSpawner
    {
        /// <summary>
        /// Спавн FX строго по ссылке из визуального пресета, без запасного ключа.
        /// Пустая/невалидная ссылка — FX пропускается (ошибку о некомплектном пресете
        /// уже написал IVisualPresetProvider).
        /// </summary>
        public static void Spawn(AssetReferencePoolItem presetReference, Vector3 position)
        {
            if (presetReference == null || !presetReference.RuntimeKeyIsValid())
            {
                return;
            }

            Service.Pool.SpawnObject(presetReference, null, position, Quaternion.identity).Forget();
        }

        public static void Spawn(AssetReferencePoolItem skinOverride, string fallbackKey, Vector3 position)
        {
            if (skinOverride != null && skinOverride.RuntimeKeyIsValid())
            {
                Service.Pool.SpawnObject(skinOverride, null, position, Quaternion.identity).Forget();
                return;
            }

            Service.Pool.SpawnObject(fallbackKey, null, position, Quaternion.identity).Forget();
        }

        /// <summary>
        /// Спавн с перекраской: если на FX-префабе есть <see cref="FxParticleTint"/>,
        /// его системы окрашиваются в переданный цвет (цвет собранных тайлов).
        /// </summary>
        public static void Spawn(AssetReferencePoolItem skinOverride, string fallbackKey, Vector3 position, Color tint)
        {
            SpawnWithTintAsync(skinOverride, fallbackKey, position, tint).Forget();
        }

        private static async UniTaskVoid SpawnWithTintAsync(
            AssetReferencePoolItem skinOverride, string fallbackKey, Vector3 position, Color tint)
        {
            var poolItem = skinOverride != null && skinOverride.RuntimeKeyIsValid()
                ? await Service.Pool.SpawnObject(skinOverride, null, position, Quaternion.identity)
                : await Service.Pool.SpawnObject(fallbackKey, null, position, Quaternion.identity);

            if (poolItem == null)
            {
                return;
            }

            var fxObject = poolItem.GetGameObject();
            if (fxObject != null && fxObject.TryGetComponent<FxParticleTint>(out var fxTint))
            {
                fxTint.Apply(tint);
            }
        }
    }
}
