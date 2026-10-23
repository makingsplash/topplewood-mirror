using System.Collections.Generic;
using NUnit.Framework;
using Ui.Components.Reveal;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-667 plan §8 T4: the intro's sound cues (<see cref="RevealSoundCues"/>, the seam <c>ScreenReveal</c> plays
    /// through). Played in order, each once; a tap that completes the intro (DOTween fires every pending callback in
    /// one frame) yields ONE cue — the latest by time — not a burst.
    /// </summary>
    [TestFixture]
    public sealed class RevealSoundCueTests
    {
        private readonly List<(string Id, float Semitones)> _played = new();

        private RevealSoundCues Stars()
        {
            _played.Clear();
            var cues = new RevealSoundCues((id, st) => _played.Add((id, st)));
            cues.Add(0.30f, "WinStar", 0f);
            cues.Add(0.42f, "WinStar", 2f);
            cues.Add(0.54f, "WinStar", 4f);
            return cues;
        }

        [Test]
        public void IntroPlayedThrough_PlaysEachCueOnce_InOrder()
        {
            var cues = Stars();
            cues.Fire(0);
            cues.Fire(1);
            cues.Fire(2);
            cues.Fire(1); // a repeated callback (Goto backwards and forward again) does not replay
            Assert.That(_played, Is.EqualTo(new[] { ("WinStar", 0f), ("WinStar", 2f), ("WinStar", 4f) }));
        }

        [Test]
        public void CompleteBeforeAnyStar_PlaysOnlyTheLatest()
        {
            var cues = Stars();
            cues.BeginComplete();
            cues.Fire(0); // what Complete(true) does: every pending callback in one frame
            cues.Fire(1);
            cues.Fire(2);
            cues.EndComplete();
            Assert.That(_played, Is.EqualTo(new[] { ("WinStar", 4f) }));
        }

        [Test]
        public void CompleteAfterTheFirstStar_AddsOnlyTheLatest()
        {
            var cues = Stars();
            cues.Fire(0);
            cues.BeginComplete();
            cues.Fire(1);
            cues.Fire(2);
            cues.EndComplete();
            Assert.That(_played, Is.EqualTo(new[] { ("WinStar", 0f), ("WinStar", 4f) }));
        }

        [Test]
        public void CompleteAfterTheIntroEnded_PlaysNothing()
        {
            var cues = Stars();
            cues.Fire(0);
            cues.Fire(1);
            cues.Fire(2);
            cues.BeginComplete();
            cues.EndComplete();
            Assert.That(_played, Has.Count.EqualTo(3));
        }

        [Test]
        public void CuesHeldByACompletion_AreNotReplayedLater()
        {
            var cues = Stars();
            cues.BeginComplete();
            cues.Fire(0);
            cues.EndComplete();
            cues.Fire(0);
            cues.Fire(1);
            Assert.That(_played, Is.EqualTo(new[] { ("WinStar", 4f) }));
        }
    }
}
