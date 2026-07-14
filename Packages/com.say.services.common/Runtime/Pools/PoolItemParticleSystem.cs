using System;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    public class PoolItemParticleSystem : PoolItemBase
    {
        [SerializeField]
        private float _lifeTime;
        
        [SerializeField]
        private ParticleSystem[] _particleSystems;

        private Vector3 _localScale;
        private float _lifeTimer;
        
        public TimeSpan LifeTime => TimeSpan.FromSeconds(_lifeTime);
        public override IResourceLocation PoolItemLocation { get; set; }

        private void Awake()
        {
            _localScale = transform.localScale;
        }

        private void Update()
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
            transform.localScale = _localScale;

            foreach (ParticleSystem system in _particleSystems)
            {
                system.Emit(1);
            }
        }

        public override void ReturnToPool()
        {
            foreach (ParticleSystem system in _particleSystems)
            {
                system.Clear();
            }
            gameObject.SetActive(false);
        }
    }
}