using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    public class PoolItemDefault : PoolItemBase
    {
        public override IResourceLocation PoolItemLocation { get; set; }

        public override GameObject GetGameObject()
        {
            return gameObject;
        }

        public override void PrepareSpawn()
        {
            gameObject.SetActive(true);
        }

        public override void ReturnToPool()
        {
            gameObject.SetActive(false);
        }
    }
}