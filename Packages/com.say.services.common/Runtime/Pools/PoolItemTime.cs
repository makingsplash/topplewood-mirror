using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    public class PoolItemTime : PoolItemBase
    {
        [SerializeField]
        private float _lifeTime;
        
        private float _lifeTimer;
        
        public override IResourceLocation PoolItemLocation { get; set; }

        private void Awake()
        {
            _lifeTimer = _lifeTime;
        }

        protected void Update()
        {
            _lifeTimer -= Time.deltaTime;

            if (_lifeTimer <= 0.0f)
            {
                Service.Pool?.ReturnToPool(this);
            }
        }

        public override GameObject GetGameObject()
        {
            return gameObject;
        }

        public override void PrepareSpawn()
        {
            _lifeTimer = _lifeTime;
            gameObject.SetActive(true);
        }

        public override void ReturnToPool()
        {
            gameObject.SetActive(false);
        }
    }
}