using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Services;
using SGS.Services.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Services.AssetPreload
{
    public class CatalogUpdateService : IService
    {
        // Total budget for the catalog check + update. If CDN is unreachable or slow this
        // prevents the background operation from hanging forever and blocking future loads.
        private static readonly TimeSpan CatalogUpdateTimeout = TimeSpan.FromSeconds(30);

        public void Init()
        {
#if !UNITY_EDITOR
            if (Data.Configs.AssetPreloadConfig != null && !Data.Configs.AssetPreloadConfig.EnableCDNUpdate)
            {
                Debug.LogWarning("[CatalogUpdate] CDN update disabled via AssetPreloadConfig.EnableCDNUpdate — skipping.");
                return;
            }
            UpdateCatalogsAndCheckRunningEvents().Forget();
#endif
        }

        public void DeInit() { }

        private async UniTask UpdateCatalogsAndCheckRunningEvents()
        {
            using var timeoutCts = new CancellationTokenSource(CatalogUpdateTimeout);
            try
            {
                await UniTask.Delay(3000, cancellationToken: timeoutCts.Token);

                var checkHandle = Addressables.CheckForCatalogUpdates();
                await checkHandle.ToUniTask(cancellationToken: timeoutCts.Token);

                if (!checkHandle.IsValid() || checkHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    if (checkHandle.IsValid())
                        Addressables.Release(checkHandle);
                    return;
                }

                if (checkHandle.OperationException != null)
                {
                    Addressables.Release(checkHandle);
                    return;
                }

                var catalogsToUpdate = checkHandle.Result;
                Addressables.Release(checkHandle);

                if (catalogsToUpdate == null || catalogsToUpdate.Count == 0)
                {
                    return;
                }

                var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate);
                await updateHandle.ToUniTask(cancellationToken: timeoutCts.Token);

                if (!updateHandle.IsValid() || updateHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    if (updateHandle.IsValid())
                        Addressables.Release(updateHandle);
                    return;
                }

                if (updateHandle.OperationException != null)
                {
                    Addressables.Release(updateHandle);
                    return;
                }

                Addressables.Release(updateHandle);
            
                var assetPreloadService = GlobalService.AssetPreloadService;
                if (assetPreloadService == null)
                    return;

                // TODO: after a catalog update, warm any asset groups the new game wants preloaded, e.g.:
                //     await assetPreloadService.PreloadAssetGroup(assetGroup);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Debug.LogWarning($"[CatalogUpdate] Catalog check/update timed out after {CatalogUpdateTimeout.TotalSeconds:F0}s. Will use cached bundles.");
            }
            catch (Exception ex)
            {
                GlobalService.DebugLog.LogError($"[CatalogUpdate] Error in UpdateCatalogsAndCheckRunningEvents: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
