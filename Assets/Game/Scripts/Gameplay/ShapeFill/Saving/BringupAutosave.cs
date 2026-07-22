using System;
using Gameplay.Core;
using Infrastructure.ApplicationTriggers;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Data-free autosave lifecycle for the isolated bringup scope. The production <c>SaveManager</c> reads
    /// <c>Data.Debug.DisableSave</c>, which requires the full SayData/PlayerPrefs bootstrap that the isolated
    /// bringup scene deliberately does not raise (RFC §2.3 R5). So bringup hooks the same app pause/focus/quit
    /// triggers and drives <see cref="IProgressSaver"/> directly — enough to exercise the F2 save round-trip.
    /// </summary>
    public sealed class BringupAutosave : IInitializable, IDisposable
    {
        private readonly IApplicationTriggers _triggers;
        private readonly IProgressSaver _progressSaver;

        public BringupAutosave(IApplicationTriggers triggers, IProgressSaver progressSaver)
        {
            _triggers = triggers;
            _progressSaver = progressSaver;
        }

        public void Initialize()
        {
            _triggers.PauseChanged += OnPauseChanged;
            _triggers.FocusChanged += OnFocusChanged;
            _triggers.IsQuitting += OnIsQuitting;
            _triggers.WantsToQuit += OnWantsToQuit;
        }

        public void Dispose()
        {
            _triggers.PauseChanged -= OnPauseChanged;
            _triggers.FocusChanged -= OnFocusChanged;
            _triggers.IsQuitting -= OnIsQuitting;
            _triggers.WantsToQuit -= OnWantsToQuit;
        }

        private void OnPauseChanged(bool paused) => _progressSaver.Save();

        private void OnFocusChanged(bool focused) => _progressSaver.Save();

        private void OnIsQuitting() => _progressSaver.Save();

        private bool OnWantsToQuit()
        {
            _progressSaver.Save();
            return true;
        }
    }
}
