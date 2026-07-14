using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    [RequireComponent(typeof(TrailRenderer))]
    public class PoolItemTrailRenderer : PoolItemBase
    {
        private TrailRenderer _trail;
        
        public override IResourceLocation PoolItemLocation { get; set; }

        private void Awake()
        {
            _trail = gameObject.GetComponent<TrailRenderer>();
        }

        public override GameObject GetGameObject()
        {
            return gameObject;
        }

        public override void PrepareSpawn()
        {
            _trail.Clear();
            gameObject.SetActive(true);
        }

        public override void ReturnToPool()
        {
            gameObject.SetActive(false);
            Service.Pool?.ReturnToPool(this);
        }
    }
}