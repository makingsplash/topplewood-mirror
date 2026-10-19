using System.Collections.Generic;
using Gameplay.Tutorial;

namespace Gameplay.Tests
{
    /// <summary>
    /// Recording <see cref="ITourView"/> for EditMode tour tests (TASK-613). Since TASK-634 the tour has no beat of its own
    /// on the win, so the fake only records the tap beats and clears.
    /// </summary>
    internal sealed class FakeTourView : ITourView
    {
        public readonly List<TapCue> Taps = new();
        public int Misses;
        public int Hides;
        public int Clears;

        public void ShowTap(TapCue cue) => Taps.Add(cue);

        public void PulseMiss() => Misses++;

        public void HideTap() => Hides++;

        public void Clear() => Clears++;
    }
}
