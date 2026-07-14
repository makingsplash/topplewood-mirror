using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    public interface IPoolItem
    {
        IResourceLocation PoolItemLocation { get; set; }
        GameObject GetGameObject();
        void PrepareSpawn();
        void ReturnToPool();
    }
}