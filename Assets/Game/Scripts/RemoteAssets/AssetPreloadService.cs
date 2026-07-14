using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Services;
using SGS.Services.DebugLog;
using SGS.Services.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Services.AssetPreload
{
    public class AssetPreloadService : IService
    {
        private const float UpdateIntervalSeconds = 1.5f;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;
        
        private readonly Dictionary<string, DownloadInfo> _activeDownloads = new();
        
        public event Action<string> OnStarted;
        public event Action<string, float> OnProgress;
        public event Action<string> OnCompleted;
        public event Action<string, string> OnFailed;

        private CancellationTokenSource _cancellationTokenSource;
        private AssetPreloadServiceDebugger _debugger;
        private float _timeSinceLastUpdate;

        public void Init()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            GlobalService.Timer.Update.AddListener(OnUpdate);

            _debugger = new AssetPreloadServiceDebugger();
            _debugger.Init(this);
        }

        public void DeInit()
        {
            GlobalService.Timer.Update.RemoveListener(OnUpdate);
            _cancellationTokenSource?.Cancel();
            
            _debugger?.DeInit();
            _debugger = null;
            
            foreach (var download in _activeDownloads.Values)
            {
                if (download.Handle.IsValid())
                    Addressables.Release(download.Handle);
            }
            _activeDownloads.Clear();
            _cancellationTokenSource?.Dispose();
        }
        
        public async UniTask<bool> IsAssetGroupLoaded(string assetGroup)
        {
            if (string.IsNullOrEmpty(assetGroup))
                return true;

            var (status, _) = await GetStatus(assetGroup);
            return status == PreloadStatus.Downloaded;
        }

        public async UniTask PreloadAssets(IAssetsCollector collector, int currentLevel, CancellationToken cancellationToken = default)
        {
            if (cancellationToken == default)
                cancellationToken = _cancellationTokenSource.Token;

            var assets = collector.CollectAssets(currentLevel);
            if (assets.Count == 0)
                return;
            
            var tasks = assets
                .Where(g => !string.IsNullOrEmpty(g))
                .Select(g => PreloadAsset(g, cancellationToken))
                .ToList();
            
            await UniTask.WhenAll(tasks);
        }
        
        public async UniTask PreloadAssetGroup(string assetGroup, CancellationToken cancellationToken = default)
        {
            if (cancellationToken == default)
                cancellationToken = _cancellationTokenSource.Token;

            await PreloadAsset(assetGroup, cancellationToken);
        }

        private async UniTask<(PreloadStatus status, float progress)> GetStatus(string label)
        {
            if (string.IsNullOrEmpty(label))
                return (PreloadStatus.NotStarted, 0f);

            if (_activeDownloads.TryGetValue(label, out var downloadInfo))
                return (PreloadStatus.Downloading, downloadInfo.Handle.PercentComplete);

            try
            {
                var locationsHandle = Addressables.LoadResourceLocationsAsync(label);
                await locationsHandle.Task;
                
                if (locationsHandle.Status != AsyncOperationStatus.Succeeded || locationsHandle.Result == null || locationsHandle.Result.Count == 0)
                {
                    Addressables.Release(locationsHandle);
                    return (PreloadStatus.NotStarted, 0f);
                }
                
                var keys = locationsHandle.Result.Select(loc => loc.PrimaryKey).ToList();
                var sizeHandle = Addressables.GetDownloadSizeAsync(keys);
                Addressables.Release(locationsHandle);
                
                await sizeHandle.Task;

                if (sizeHandle.Status == AsyncOperationStatus.Failed)
                {
                    Addressables.Release(sizeHandle);
                    return (PreloadStatus.NotStarted, 0f);
                }

                var downloadSize = sizeHandle.Result;
                Addressables.Release(sizeHandle);

                return downloadSize > 0 ? (PreloadStatus.NotStarted, 0f) : (PreloadStatus.Downloaded, 1f);
            }
            catch (Exception ex)
            {
                GlobalService.DebugLog.Log($"{ex.Message}", DebugLogType.AssetPreload);
                return (PreloadStatus.NotStarted, 0f);
            }
        }

        private async UniTask PreloadAsset(string assetLabel, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(assetLabel))
                return;
        
            try
            {
                var status = await GetStatus(assetLabel);
                if (cancellationToken.IsCancellationRequested || status.status != PreloadStatus.NotStarted)
                    return;
        
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
        
                    OnStarted?.Invoke(assetLabel);
        
                    AsyncOperationHandle handle = default;
                    try
                    {
                        var locationsHandle = Addressables.LoadResourceLocationsAsync(assetLabel);
                        await locationsHandle.Task;
        
                        if (locationsHandle.Status != AsyncOperationStatus.Succeeded ||
                            locationsHandle.Result == null ||
                            locationsHandle.Result.Count == 0)
                        {
                            Addressables.Release(locationsHandle);
        
                            if (attempt >= MaxRetries)
                            {
                                OnFailed?.Invoke(assetLabel, "No locations found");
                                return;
                            }
        
                            await UniTask.Delay(RetryDelayMs, cancellationToken: cancellationToken);
                            continue;
                        }
        
                        var keys = locationsHandle.Result.Select(loc => loc.PrimaryKey).ToList();
                        Addressables.Release(locationsHandle);
        
                        handle = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union);
        
                        _activeDownloads[assetLabel] = new DownloadInfo
                        {
                            Handle = handle
                        };
        
                        await handle.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        ReleaseHandle(handle);
                        RemoveFromActiveDownloads(assetLabel);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt >= MaxRetries)
                        {
                            OnFailed?.Invoke(assetLabel, ex.Message);
                            ReleaseHandle(handle);
                            RemoveFromActiveDownloads(assetLabel);
                            return;
                        }
        
                        ReleaseHandle(handle);
                        RemoveFromActiveDownloads(assetLabel);
                        await UniTask.Delay(RetryDelayMs, cancellationToken: cancellationToken);
                        continue;
                    }
        
                    RemoveFromActiveDownloads(assetLabel);
        
                    if (!handle.IsValid())
                    {
                        if (attempt >= MaxRetries)
                        {
                            OnFailed?.Invoke(assetLabel, "Handle invalid after completion");
                            return;
                        }
                        await UniTask.Delay(RetryDelayMs, cancellationToken: cancellationToken);
                        continue;
                    }
        
                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.OperationException == null)
                    {
                        OnProgress?.Invoke(assetLabel, 1f);
                        OnCompleted?.Invoke(assetLabel);
                        ReleaseHandle(handle);
                        return;
                    }

                    var errorMsg = handle.OperationException?.Message ?? "Unknown error";
                    
                    if (attempt >= MaxRetries)
                    {
                        OnFailed?.Invoke(assetLabel, errorMsg);
                        ReleaseHandle(handle);
                        return;
                    }
        
                    ReleaseHandle(handle);
                    await UniTask.Delay(RetryDelayMs, cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(assetLabel, ex.Message);
            }
        }

        private float GetNormalizedProgress(string labelName, float currentProgress)
        {
            if (!_activeDownloads.TryGetValue(labelName, out var downloadInfo))
                return currentProgress;

            var initialProgress = downloadInfo.InitialProgress;
            if (initialProgress >= 1f)
                return 1f;

            return Mathf.Clamp01((currentProgress - initialProgress) / (1f - initialProgress));
        }

        private void OnUpdate()
        {
            _timeSinceLastUpdate += Time.deltaTime;
            
            if (_timeSinceLastUpdate < UpdateIntervalSeconds)
                return;

            _timeSinceLastUpdate = 0f;

            var keys = new List<string>(_activeDownloads.Keys);
            
            foreach (var label in keys)
            {
                if (!_activeDownloads.TryGetValue(label, out var info))
                    continue;

                if (!info.Handle.IsValid() || info.Handle.IsDone)
                    continue;

                if (info.InitialProgress == 0)
                {
                    info.InitialProgress = info.Handle.PercentComplete;
                    _activeDownloads[label] = info;
                    OnProgress?.Invoke(label, 0f);
                    continue;
                }

                float norm = GetNormalizedProgress(label, info.Handle.PercentComplete);
                OnProgress?.Invoke(label, norm);
            }
        }

        private void ReleaseHandle(AsyncOperationHandle handle)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        private void RemoveFromActiveDownloads(string assetLabelName)
        {
            _activeDownloads.Remove(assetLabelName);
        }

#if GPIE_DEVELOPMENT
        public Dictionary<string, float> GetActiveDownloads()
        {
            var result = new Dictionary<string, float>();
            foreach (var download in _activeDownloads)
            {
                if (download.Value.Handle.IsValid() && !download.Value.Handle.IsDone)
                {
                    result[download.Key] = download.Value.Handle.PercentComplete;
                }
            }
            return result;
        }

        public async UniTask<Dictionary<string, PreloadStatus>> GetLoadedPacks()
        {
            var result = new Dictionary<string, PreloadStatus>();
            var allLabels = new HashSet<string>();

            // TODO: populate allLabels with the new game's preloadable asset groups.

            foreach (var label in allLabels)
            {
                var (status, _) = await GetStatus(label);
                result[label] = status;
            }
            
            return result;
        }
#endif

    }
    
    public struct DownloadInfo
    {
        public AsyncOperationHandle Handle;
        public float InitialProgress;
    }
    
    public enum PreloadStatus
    {
        NotStarted = 0,
        Downloading = 1,
        Downloaded = 2
    }

}
