using System.Collections.Generic;

namespace Ui.Components.Reveal
{
    /// <summary>Timing facts of one track, as the resolver sees them (no Unity objects — testable headless).</summary>
    public readonly struct RevealTrackTiming
    {
        public readonly int FirstAtMs;
        public readonly int IntroEndMs;
        public readonly int SkipShiftMs;
        public readonly bool Active;
        public readonly bool UnlocksInput;

        public RevealTrackTiming(int firstAtMs, int introEndMs, int skipShiftMs, bool active, bool unlocksInput)
        {
            FirstAtMs = firstAtMs;
            IntroEndMs = introEndMs;
            SkipShiftMs = skipShiftMs;
            Active = active;
            UnlocksInput = unlocksInput;
        }
    }

    /// <summary>
    /// Pure timing rules of a screen reveal (TASK-425):
    /// <list type="bullet">
    /// <item>A skipped (inactive) track with <c>SkipShiftMs</c> pulls every OTHER track that starts at or after it
    /// earlier by that amount; shifts of several skipped tracks add up.</item>
    /// <item>Input unlocks at the latest shifted intro end among active <c>UnlocksInput</c> tracks + the input lag;
    /// if none of them is active — at the latest shifted intro end of all active tracks + the lag.</item>
    /// </list>
    /// </summary>
    public static class RevealTimeline
    {
        /// <returns>Unlock time, ms. <paramref name="shiftsMs"/> receives the shift to SUBTRACT from each track.</returns>
        public static int Resolve(IReadOnlyList<RevealTrackTiming> tracks, int inputLagMs, int[] shiftsMs)
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                var shift = 0;
                for (var j = 0; j < tracks.Count; j++)
                {
                    var skipped = tracks[j];
                    if (j == i || skipped.Active || skipped.SkipShiftMs <= 0)
                        continue;
                    if (skipped.FirstAtMs <= tracks[i].FirstAtMs)
                        shift += skipped.SkipShiftMs;
                }
                shiftsMs[i] = shift;
            }

            var unlockEnd = -1;
            var anyEnd = 0;
            for (var i = 0; i < tracks.Count; i++)
            {
                if (!tracks[i].Active)
                    continue;
                var end = tracks[i].IntroEndMs - shiftsMs[i];
                if (end > anyEnd)
                    anyEnd = end;
                if (tracks[i].UnlocksInput && end > unlockEnd)
                    unlockEnd = end;
            }

            return (unlockEnd >= 0 ? unlockEnd : anyEnd) + inputLagMs;
        }
    }
}
