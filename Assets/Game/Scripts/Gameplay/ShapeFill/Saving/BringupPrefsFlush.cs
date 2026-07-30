using System;
using Infrastructure.ApplicationTriggers;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Data-free prefs-flush lifecycle for the isolated bringup scope (TASK-044 §8, replaces <c>BringupAutosave</c>).
    /// Production flushes prefs from <c>SaveManager</c>, but that reads <c>Data.Debug.DisableSave</c> which needs
    /// the SayData/PlayerPrefs bootstrap the bringup scene deliberately omits (RFC R5). So bringup hooks the same
    /// pause/focus/quit triggers and drives <c>SaveDataIfChanged()</c> directly — the belt-and-suspenders flush
    /// (the WIN checkpoint already flushes; this catches backgrounding) so campaign progress survives a kill
    /// without a normal exit.
    /// </summary>
    public sealed class BringupPrefsFlush : IInitializable, IDisposable
    {
        private readonly IApplicationTriggers _triggers;

        public BringupPrefsFlush(IApplicationTriggers triggers)
        {
            _triggers = triggers;
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

        private static void Flush() => PlayerPrefsCollection.Instance?.SaveDataIfChanged();

        private void OnPauseChanged(bool paused) => Flush();

        private void OnFocusChanged(bool focused) => Flush();

        private void OnIsQuitting() => Flush();

        private bool OnWantsToQuit()
        {
            Flush();
            return true;
        }
    }
}
