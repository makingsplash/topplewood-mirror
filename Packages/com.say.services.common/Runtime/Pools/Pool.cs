using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using SGS.Services.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace SGS.Services.Pools
{
    public class Pool : IService
    {
        private readonly Dictionary<IResourceLocation, List<IPoolItem>> _pool = new ();
        private readonly Dictionary<string, IResourceLocation> _keysLocations = new ();
        private readonly Dictionary<AssetReferencePoolItem, IResourceLocation> _referenceLocations = new ();

        private Vector3 _defaultPosition = Vector3.one * 1000;
        private Transform _invisibleTransform;
        private Transform _rootTransform;
        private readonly Dictionary<IResourceLocation, Transform> _itemTransform = new ();
        
        public Pool()
        {
            var rootObject = new GameObject("Pool");
            _rootTransform = rootObject.transform;
            var invisibleObject = new GameObject("PoolInvisibleRoot");
            invisibleObject.SetActive(false);
            _invisibleTransform = invisibleObject.transform;

            PreloadItems();
        }

        public void Init()
        {
        }

        public void DeInit()
        {
            foreach (var (_, poolList) in _pool)
            {
                foreach (IPoolItem poolItem in poolList)
                {
                    if (IsDestroyed(poolItem))
                    {
                        continue;
                    }
                    
                    GameObject itemObject = poolItem.GetGameObject();
                    if (itemObject == null)
                    {
                        continue;
                    }
                    
                    Object.Destroy(itemObject);
                }
                poolList.Clear();
            }
            _pool.Clear();
            _itemTransform.Clear();

            /*
            foreach (var (_, location) in _keysLocations)
            {
                Addressables.Release(location);
            }
            */
            _keysLocations.Clear();
            _referenceLocations.Clear();
        }

        private void PreloadItems()
        {
            List<IPoolItem> tempList = new List<IPoolItem>(5);

            // TODO: preload
            //PreloadItem(PoolItemType.EnemyBlood, 3, tempList);
        }
        
        private async void PreloadItem(string type, int count, List<IPoolItem> tempList)
        {
            for (int i = 0; i < count; i++)
            {
                tempList.Add(await GetItem(type));
            }
            for (int i = 0; i < count; i++)
            {
                tempList[i].ReturnToPool();
            }
            
            tempList.Clear();
        }

        private async UniTask<IPoolItem> GetItem(string assetKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(assetKey))
            {
                return null;
            }

            IResourceLocation location;
            if (_keysLocations.ContainsKey(assetKey))
            {
                location = _keysLocations[assetKey];
            }
            else
            {
                IList<IResourceLocation> locations = cancellationToken == default 
                    ? await Addressables.LoadResourceLocationsAsync(assetKey)
                    : await Addressables.LoadResourceLocationsAsync(assetKey).WithCancellation(cancellationToken);
                location = locations.First();
                _keysLocations.Add(assetKey, location);
            }
            
            IPoolItem poolItem = GetItem(location);
            return poolItem;
        }
        
        private async UniTask<IPoolItem> GetItem(AssetReferencePoolItem assetReference, CancellationToken cancellationToken = default)
        {
            IResourceLocation location;
            if (_referenceLocations.ContainsKey(assetReference))
            {
                location = _referenceLocations[assetReference];
            }
            else
            {
                IList<IResourceLocation> locations = cancellationToken == default 
                    ? await Addressables.LoadResourceLocationsAsync(assetReference)
                    : await Addressables.LoadResourceLocationsAsync(assetReference).WithCancellation(cancellationToken);
                location = locations.First();
                _referenceLocations.Add(assetReference, location);
            }
            
            IPoolItem poolItem = GetItem(location);
            return poolItem;
        }

        private IPoolItem GetItem(IResourceLocation locator)
        {
            IPoolItem poolItem = null;
            if (_pool.ContainsKey(locator))
            {
                List<IPoolItem> poolList = _pool[locator];
                if (poolList.Count > 0)
                {
                    poolItem = poolList[poolList.Count - 1];
                    poolList.RemoveAt(poolList.Count - 1);
                }
            }
            
            if (poolItem == null)
            {
                GameObject gameObject = Addressables.InstantiateAsync(locator, _defaultPosition, Quaternion.identity, _invisibleTransform).WaitForCompletion();
                poolItem = gameObject.GetComponent<IPoolItem>();
                poolItem.PoolItemLocation = locator;
            }
            
            return poolItem;
        }

        public async UniTask<IPoolItem> SpawnObject(string assetKey, CancellationToken cancellationToken = default)
        {
            return await SpawnObject(assetKey, null, Vector3.zero, Quaternion.identity, cancellationToken);
        }

        public async UniTask<IPoolItem> SpawnObject(string assetKey, Transform parent, CancellationToken cancellationToken = default)
        {
            return await SpawnObject(assetKey, parent, Vector3.zero, Quaternion.identity, cancellationToken);
        }

        public async UniTask<IPoolItem> SpawnObject(string assetKey, Transform parent, Vector3 position, Quaternion rotation, CancellationToken cancellationSource = default)
        {
            IPoolItem poolItem = await GetItem(assetKey, cancellationSource);
            AfterSpawn(poolItem, parent, position, rotation);
            
            return poolItem;
        }
        
        public async UniTask<IPoolItem> SpawnObject(AssetReferencePoolItem assetReference, CancellationToken cancellationToken = default)
        {
            return await SpawnObject(assetReference, null, Vector3.zero, Quaternion.identity, cancellationToken);
        }

        public async UniTask<IPoolItem> SpawnObject(AssetReferencePoolItem assetReference, Transform parent, CancellationToken cancellationToken = default)
        {
            return await SpawnObject(assetReference, parent, Vector3.zero, Quaternion.identity, cancellationToken);
        }
        
        public async UniTask<IPoolItem> SpawnObject(AssetReferencePoolItem assetReference, Transform parent, Vector3 position, Quaternion rotation, CancellationToken cancellationToken = default)
        {
            IPoolItem poolItem = await GetItem(assetReference, cancellationToken);
            AfterSpawn(poolItem, parent, position, rotation);
            
            return poolItem;
        }

        private void AfterSpawn(IPoolItem poolItem, Transform parent, Vector3 position, Quaternion rotation)
        {
            if (IsDestroyed(poolItem))
            {
                return;
            }
            
            GameObject itemObject = poolItem.GetGameObject();
            if (itemObject == null)
            {
                return;
            }
            
            itemObject.transform.SetParent(parent, false);
            itemObject.transform.position = position;
            itemObject.transform.rotation = rotation;

            poolItem.PrepareSpawn();
        }
        
        public void ReturnToPool(IPoolItem poolItem)
        {
            if (IsDestroyed(poolItem))
            {
                return;
            }
            
            poolItem.ReturnToPool();
            IResourceLocation location = poolItem.PoolItemLocation;
            List<IPoolItem> poolList;
            Transform poolRoot;

            if (_pool.ContainsKey(location))
            {
                poolList = _pool[location];
                poolRoot = _itemTransform[location];
            }
            else
            {
                poolList = new List<IPoolItem>();
                _pool.Add(location, poolList);

                var poolRootObject = new GameObject(Path.GetFileName(location.PrimaryKey));
                poolRoot = poolRootObject.transform;
                poolRoot.SetParent(_rootTransform);
                _itemTransform.Add(location, poolRoot);
            }
            
            GameObject itemObject = poolItem.GetGameObject();
            if (itemObject && itemObject.transform && poolRoot)
            {
                itemObject.transform.SetParent(poolRoot);
            }

            poolList.Add(poolItem);
        }
        
        private static bool IsDestroyed(IPoolItem poolItem)
        {
            if (poolItem == null)
            {
                return true;
            }

            return poolItem is Object unityObject && unityObject == null;
        }
    }
}
