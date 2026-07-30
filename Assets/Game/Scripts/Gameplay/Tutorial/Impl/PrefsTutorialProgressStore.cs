using System.Collections.Generic;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Bringup <see cref="ITutorialProgressStore"/> (TASK-049 RFC §Q1). Owns its own
    /// <see cref="PlayerPrefsListString"/> on a SEPARATE key <c>Tutorial.Completed</c> (default empty ⇒ no
    /// migration) — deliberately, NOT merely because the R5-isolated scene raises no <c>Data</c>/<c>SayData</c>.
    /// A separate key isolates FTUE: dev-replaying the tour in the gray-box scene must NOT mark the tutorial
    /// completed for the real player. This is the OPPOSITE of <c>PrefsCampaignStore</c>, which deliberately shares
    /// the real canon key <c>Meta.CurrentLevel</c> so campaign progress carries between bringup and prod — there
    /// carry-over is wanted; here it is not. Shares the real prefs backend the bringup scope Inits, so completion
    /// survives Stop→Play. Each mutation flushes durably, mirroring the campaign store's flush discipline.
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
