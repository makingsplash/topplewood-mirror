using System;
using DataStorage;
using Infrastructure.ApplicationTriggers;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;
using VContainer.Unity;
using Game.Services.LogService;

namespace Game.Infrastructure.Saving
{
    /// <summary>
    /// Lifecycle prefs-flush (TASK-044): hooks app pause/focus/quit and throttles a durable PlayerPrefs flush.
    /// The old in-level snapshot save was retired (BUG-2, write-only), so "what to save" is now simply all
    /// dirty PlayerPrefs (gold/hearts/campaign pointer/…) via the Say wrapper. The campaign WIN already flushes
    /// its own checkpoint; this is the belt-and-suspenders flush that catches backgrounding.
    /// </summary>
    public sealed class SaveManager : IInitializable, IDisposable
    {
        private readonly IApplicationTriggers _applicationTriggers;
        private readonly ILogService _logService;

        private float _lastSaveTime;
        private const float TimeBetweenSave = 1f;

        public SaveManager(
            IApplicationTriggers applicationTriggers,
            ILogService logService
        )
        {
            _applicationTriggers = applicationTriggers;
            _logService = logService;
        }

        public void Initialize()
        {
            _applicationTriggers.PauseChanged += OnPauseChanged;
            _applicationTriggers.FocusChanged += OnFocusChanged;
            _applicationTriggers.WantsToQuit += OnWantsToQuit;
            _applicationTriggers.IsQuitting += OnIsQuitting;
        }

        public void Dispose()
        {
            _applicationTriggers.PauseChanged -= OnPauseChanged;
            _applicationTriggers.FocusChanged -= OnFocusChanged;
            _applicationTriggers.WantsToQuit -= OnWantsToQuit;
            _applicationTriggers.IsQuitting -= OnIsQuitting;
        }

        private void OnPauseChanged(bool pauseStatus) => SaveProgress();

        private void OnFocusChanged(bool hasFocus) => SaveProgress();

        private void OnIsQuitting() => SaveProgress();

        private bool OnWantsToQuit()
        {
            SaveProgress();
            return true;
        }

        private void SaveProgress()
        {
            if (Data.Debug.DisableSave.Value)
            {
                _logService.Log(LogLevel.Info, "Saving skipped by debug flag.", LogCategory.SaveManager);
                return;
            }

            var delta = Time.realtimeSinceStartup - _lastSaveTime;
            if (delta < TimeBetweenSave)
            {
                _logService.Log(LogLevel.Info, $"Skip saving. Delta = {delta} = {Time.realtimeSinceStartup} - {_lastSaveTime}", LogCategory.SaveManager);
                return;
            }

            _lastSaveTime = Time.realtimeSinceStartup;

            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
            _logService.Log(LogLevel.Info, $"Prefs flush tick. Delta = {delta}", LogCategory.SaveManager);
        }
    }
}
