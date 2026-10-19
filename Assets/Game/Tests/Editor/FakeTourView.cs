using System.Collections.Generic;
using Gameplay.Tutorial;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// Recording <see cref="ITourView"/> for EditMode tour tests (TASK-613). The "Совпало" number it returns from
    /// <see cref="PlayMatch"/> runs until the test ends it with <see cref="FinishMatch"/> — so a test decides exactly
    /// when the beat is over instead of waiting on a real timer.
    /// </summary>
    internal sealed class FakeTourView : ITourView
    {
        public readonly List<TapCue> Taps = new();
        public int Misses;
        public int Hides;
        public int Clears;
        public int MatchSubscriptions;
        public int MatchDisposals;
        public MatchCue LastMatch;

        private Observer<Unit> _match;

        public void ShowTap(TapCue cue) => Taps.Add(cue);

        public void PulseMiss() => Misses++;

        public void HideTap() => Hides++;

        public Observable<Unit> PlayMatch(MatchCue cue)
        {
            LastMatch = cue;
            return Observable.Create<Unit>(observer =>
            {
                MatchSubscriptions++;
                _match = observer;
                return Disposable.Create(() => MatchDisposals++);
            });
        }

        /// <summary>End the running "Совпало" number (emit + complete), as the real view's timer would.</summary>
        public void FinishMatch()
        {
            var match = _match;
            _match = null;
            match?.OnNext(Unit.Default);
            match?.OnCompleted();
        }

        public void Clear() => Clears++;
    }
}
