using System.Collections.Generic;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Bringup <see cref="ITutorialProgressStore"/> (TASK-049 RFC §Q1): the R5-isolated scene raises no
    /// <c>Data</c>/<c>SayData</c>, so it owns its own <see cref="PlayerPrefsListString"/> on a fresh key
    /// <c>Tutorial.Completed</c> (default empty ⇒ no migration). Shares the real prefs backend the bringup scope
    /// Inits, so completion survives Stop→Play. Each mutation flushes durably, mirroring the campaign store.
    /// </summary>
    public sealed class PrefsTutorialProgressStore : ITutorialProgressStore
    {
        private readonly PlayerPrefsListString _completed = new("Tutorial.Completed", new List<string>());

        public bool IsCompleted(string tutorialId)
        {
            var value = _completed.Value;
            return value != null && value.Contains(tutorialId);
        }

        public void MarkCompleted(string tutorialId)
        {
            if (IsCompleted(tutorialId))
                return;
            _completed.Add(tutorialId);                           // writes through to the prefs backend
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();  // durable flush (survives Stop→Play / kill)
        }

        public void Reset(string tutorialId)
        {
            if (!IsCompleted(tutorialId))
                return;
            _completed.Remove(tutorialId);
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }
    }
}
