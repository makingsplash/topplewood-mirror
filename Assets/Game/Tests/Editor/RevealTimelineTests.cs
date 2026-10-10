using NUnit.Framework;
using Ui.Components.Reveal;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-425: timing rules of the data-driven screen reveal, on the win-screen timeline of 01-game-designer §4.1.
    /// The expected unlock times are the designer's numbers per branch (1250 / 800 / 1170), not values read back
    /// from the implementation.
    /// </summary>
    [TestFixture]
    public sealed class RevealTimelineTests
    {
        private const int InputLag = 20;

        // Order: scrim, title, glow (skip-shift 450), coins, amount, x2 (skip-shift 80, unlocks), next (unlocks).
        private static RevealTrackTiming[] Win(bool reward, bool x2) => new[]
        {
            new RevealTrackTiming(0, 200, 0, true, false),
            new RevealTrackTiming(80, 380, 0, true, false),
            new RevealTrackTiming(250, 650, 450, reward, false),
            new RevealTrackTiming(320, 620, 0, reward, false),
            new RevealTrackTiming(520, 1250, 0, reward, false),
            new RevealTrackTiming(900, 1150, 80, x2, true),
            new RevealTrackTiming(980, 1230, 0, true, true),
        };

        private static int Resolve(RevealTrackTiming[] tracks, out int[] shifts)
        {
            shifts = new int[tracks.Length];
            return RevealTimeline.Resolve(tracks, InputLag, shifts);
        }

        [Test]
        public void FullWin_UnlocksAfterLastButton_NotAfterLaterPunch()
        {
            // The amount's punch ends at 1250, later than the buttons (1230): it must not delay input.
            Assert.That(Resolve(Win(reward: true, x2: true), out _), Is.EqualTo(1250));
        }

        [Test]
        public void Replay_NoReward_PullsButtonsEarlierBy450()
        {
            var unlock = Resolve(Win(reward: false, x2: true), out var shifts);
            Assert.That(unlock, Is.EqualTo(800));
            Assert.That(shifts[5], Is.EqualTo(450));
            Assert.That(shifts[6], Is.EqualTo(450));
            Assert.That(shifts[1], Is.EqualTo(0), "the title starts before the skipped glow and keeps its time");
        }

        [Test]
        public void NoX2_ContinueTakesItsSlot()
        {
            var unlock = Resolve(Win(reward: true, x2: false), out var shifts);
            Assert.That(unlock, Is.EqualTo(1170));
            Assert.That(shifts[6], Is.EqualTo(80));
        }

        [Test]
        public void BothBranchesSkipped_ShiftsAddUp()
        {
            var unlock = Resolve(Win(reward: false, x2: false), out var shifts);
            Assert.That(shifts[6], Is.EqualTo(530));
            Assert.That(unlock, Is.EqualTo(1230 - 530 + InputLag));
        }

        [Test]
        public void TapCatcher_BringsItsCanvasRenderer()
        {
            // unity-gotchas: a Graphic subclass without RequireComponent(CanvasRenderer) throws in the editor on the
            // first raycast. Hit in TASK-425's first play-mode run; this pins the fix.
            var go = new UnityEngine.GameObject("catcher", typeof(UnityEngine.RectTransform));
            try
            {
                go.AddComponent<RevealTapCatcher>();
                Assert.That(go.GetComponent<UnityEngine.CanvasRenderer>() != null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NoUnlockingTrackActive_FallsBackToLastIntroEnd()
        {
            var tracks = new[]
            {
                new RevealTrackTiming(0, 250, 0, true, false),
                new RevealTrackTiming(100, 450, 0, true, false),
                new RevealTrackTiming(450, 700, 0, false, true),
            };
            Assert.That(Resolve(tracks, out _), Is.EqualTo(450 + InputLag));
        }
    }
}
