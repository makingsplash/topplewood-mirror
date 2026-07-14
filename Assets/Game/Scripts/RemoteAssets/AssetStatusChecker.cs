using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Services;
using SGS.Services.DebugLog;
using SGS.Services.Interfaces;

namespace Services.AssetPreload
{
    public class AssetStatusChecker : IService
    {
        private readonly Dictionary<string, bool> _levelPausedStates = new();
        private readonly HashSet<string> _manualPausedLevels = new();
        private readonly HashSet<string> _checkingLevels = new();
        
        public event Action<string> OnLevelResumed;
        
        
        public void Init()
        { }

        public void DeInit()
        {
            _levelPausedStates.Clear();
            _manualPausedLevels.Clear();
            _checkingLevels.Clear();
        }

        public bool IsLevelPaused(string levelName)
        {
            if (_manualPausedLevels.Contains(levelName))
                return true;
            
            string assetGroup = levelName;
            
            if (string.IsNullOrEmpty(assetGroup))
                return false;
            
            if (_checkingLevels.Contains(levelName))
                return _levelPausedStates.GetValueOrDefault(levelName, true);
            
            _levelPausedStates.TryAdd(levelName, true);
            _checkingLevels.Add(levelName);
            
            CheckOfferAssetStatusAsync(levelName, assetGroup).Forget();
            
            return _levelPausedStates.GetValueOrDefault(levelName, true);
        }
        

        private void SetLevelPaused(string levelName, bool paused)
        {
            bool wasInDictionary = _levelPausedStates.TryGetValue(levelName, out var currentState);
            bool wasPaused = wasInDictionary && currentState;
            
            if (wasInDictionary && wasPaused == paused)
                return;
            
            _levelPausedStates[levelName] = paused;
            
            if (paused)
            {
                GlobalService.DebugLog.Log($"Level {levelName} paused - assets not loaded", DebugLogType.AssetPreload);
            }
            else
            {
                GlobalService.DebugLog.Log($"Level {levelName} resumed - assets loaded", DebugLogType.AssetPreload);
                OnLevelResumed?.Invoke(levelName);
            }
        }
        

        private async UniTaskVoid CheckOfferAssetStatusAsync(string offerId, string assetGroup)
        {
            try
            {
                if (_manualPausedLevels.Contains(offerId))
                    return;
                
                var assetPreloadService = GlobalService.AssetPreloadService;
                if (assetPreloadService == null)
                    return;
                
                var isLoaded = await assetPreloadService.IsAssetGroupLoaded(assetGroup);
                SetLevelPaused(offerId, !isLoaded);
            }
            finally
            {
                _checkingLevels.Remove(offerId);
            }
        }
        
        
#if GPIE_DEVELOPMENT
        public void DebugPauseLevel(string levelName)
        {
            _manualPausedLevels.Add(levelName);
            SetLevelPaused(levelName, true);
            GlobalService.DebugLog.Log($"Level {levelName} manually paused (debug)", DebugLogType.AssetPreload);
        }
        
        public void DebugResumeLevel(string levelName)
        {
            _manualPausedLevels.Remove(levelName);
            SetLevelPaused(levelName, false);
            GlobalService.DebugLog.Log($"Level {levelName} manually resumed (debug)", DebugLogType.AssetPreload);
        }
#endif
    }
}

