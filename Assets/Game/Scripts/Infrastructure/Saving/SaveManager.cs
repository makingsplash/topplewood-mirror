using System;
using DataStorage;
using Infrastructure.ApplicationTriggers;
using UnityEngine;
using VContainer.Unity;
using Game.Services.LogService;

namespace Game.Infrastructure.Saving
{
    /// <summary>
    /// Autosave lifecycle skeleton: hooks app pause/focus/quit triggers and throttles saves.
    /// The concrete "what to save" was gameplay-specific and was removed with the TileSort game;
    /// plug the new game's save call into <see cref="SaveProgress"/>.
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

            // TODO: plug the new game's progress serialization here.
            _logService.Log(LogLevel.Info, $"Autosave tick. Delta = {delta}", LogCategory.SaveManager);
        }
    }
}
