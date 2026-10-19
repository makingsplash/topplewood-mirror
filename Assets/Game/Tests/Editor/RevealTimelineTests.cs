using NUnit.Framework;
using Ui.Components.Reveal;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-425: timing rules of the data-driven screen reveal. The expected unlock times are the designers' numbers,
    /// not values read back from the implementation.
    /// <list type="bullet">
    /// <item><see cref="Win"/> — the live win screen since TASK-612 (ui-composer 05 §1): no branches, three
    /// glow+star pairs, CONTINUE unlocks at 900 + 250 + inputLag 20 = 1170.</item>
    /// <item><see cref="Branched"/> — the skip-shift mechanism, exercised on the pre-TASK-612 win timeline
    /// (01-game-designer TASK-425 §4.1: reward row and ×2 as optional branches, 1250 / 800 / 1170). No screen uses
    /// branches today; the rule stays in the reveal and is pinned here on a timeline whose numbers were designed.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class RevealTimelineTests
    {
        private const int InputLag = 20;

        // design/screens/WinScreen.json (TASK-612). Order: scrim, vignette, title, glowL, glowR, glowM, starL, starR,
        // starM, next (unlocks). Glow "in" 400 ms, star "in" 300 ms; loops are idle, not intro.
        private static RevealTrackTiming[] Win() => new[]
        {
            new RevealTrackTiming(0, 200, 0, true, false),
            new RevealTrackTiming(0, 200, 0, true, false),
            new RevealTrackTiming(80, 380, 0, true, false),
            new RevealTrackTiming(250, 650, 0, true, false),
            new RevealTrackTiming(490, 890, 0, true, false),
            new RevealTrackTiming(370, 770, 0, true, false),
            new RevealTrackTiming(300, 600, 0, true, false),
            new RevealTrackTiming(540, 840, 0, true, false),
            new RevealTrackTiming(420, 720, 0, true, false),
            new RevealTrackTiming(900, 1150, 0, true, true),
        };

        // Order: scrim, title, glow (skip-shift 450), coins, amount, x2 (skip-shift 80, unlocks), next (unlocks).
        private static RevealTrackTiming[] Branched(bool reward, bool x2) => new[]
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
        public void Win_ThreeStars_UnlocksAtContinueLanding()
        {
            var unlock = Resolve(Win(), out var shifts);
            Assert.That(unlock, Is.EqualTo(1170));
            Assert.That(shifts, Is.All.EqualTo(0), "no branch is skipped on the star win screen");
        }

        [Test]
        public void Branched_Full_UnlocksAfterLastButton_NotAfterLaterPunch()
        {
            // The amount's punch ends at 1250, later than the buttons (1230): it must not delay input.
            Assert.That(Resolve(Branched(reward: true, x2: true), out _), Is.EqualTo(1250));
        }

        [Test]
        public void Branched_SkippedBlock_PullsButtonsEarlierBy450()
        {
            var unlock = Resolve(Branched(reward: false, x2: true), out var shifts);
            Assert.That(unlock, Is.EqualTo(800));
            Assert.That(shifts[5], Is.EqualTo(450));
            Assert.That(shifts[6], Is.EqualTo(450));
            Assert.That(shifts[1], Is.EqualTo(0), "the title starts before the skipped glow and keeps its time");
        }

        [Test]
        public void Branched_SkippedButton_NextTakesItsSlot()
        {
            var unlock = Resolve(Branched(reward: true, x2: false), out var shifts);
            Assert.That(unlock, Is.EqualTo(1170));
            Assert.That(shifts[6], Is.EqualTo(80));
        }

        [Test]
        public void Branched_BothSkipped_ShiftsAddUp()
        {
            var unlock = Resolve(Branched(reward: false, x2: false), out var shifts);
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
