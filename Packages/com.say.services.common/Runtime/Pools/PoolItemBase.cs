using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    /// <summary>
    /// Специально сделан абстрактный базовый класс, который реализует интерфес IPoolItem от которого должны
    /// наследоваться все остальные PoolItem для того что бы можно было завести общий AssetReferencePoolItem
    /// для фильтрации референсов адрессаблов в редакторе. 
    /// </summary>
    public abstract class PoolItemBase: MonoBehaviour, IPoolItem
    {
        public abstract IResourceLocation PoolItemLocation { get; set; }
        public abstract GameObject GetGameObject();

        public abstract void PrepareSpawn();

        public abstract void ReturnToPool();
    }
}