using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-613 plan §8 T1–T3, re-cut by TASK-634 plan §7 T5: the tour is marked completed only on the WIN (it used to be
    /// marked on the last release, before the verdict — <c>TutorialRunner.CompleteTutorial</c> pre-TASK-613; TASK-613 tied
    /// it to its own "Совпало" beat, which is gone), a loss after the last tap replays the tour, and the HUD focus flag
    /// follows the tour's life.
    /// </summary>
    [TestFixture]
    public sealed class TutorialRunnerTourTests
    {
        private TourRig _rig;

        [SetUp]
        public void SetUp() => _rig = new TourRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void Runner_MarksCompleted_OnlyOnWon()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False,
                "the last release is not the end of the tour — the win is");
            Assert.That(_rig.Analytics.Steps, Is.EqualTo(new[] { "tap_1", "tap_2", "win" }),
                "the closing step is entered (and logged) right after the last tap");

            _rig.Resolve(GameOutcome.Won);

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.True);
            Assert.That(_rig.Analytics.Completes, Is.EqualTo(1));
        }

        [Test]
        public void Runner_Completion_DoesNotClearTheView()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();
            var clearsBefore = _rig.View.Clears;

            _rig.Resolve(GameOutcome.Won);

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.True);
            Assert.That(_rig.View.Clears, Is.EqualTo(clearsBefore),
                "completion must not Clear the view: the tap steps already faded their own visuals out");
        }

        [Test]
        public void Runner_LostAfterLastTap_ReplaysTour_NotCompleted()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();

            _rig.Resolve(GameOutcome.Lost);
            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False, "a loss never completes the tour");

            _rig.Reload(); // Lost → "Try again" reloads the level in place
            _rig.Model.SetFieldAtRest(true);

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False);
            Assert.That(_rig.View.Taps.Count, Is.EqualTo(3), "tap 1, tap 2, then tap 1 again after the reload");
            Assert.That(_rig.View.Taps[2].Origin, Is.EqualTo(TourRig.BottomPos));
        }

        [Test]
        public void Runner_WonWithoutTheTaps_DoesNotCompleteTheTour()
        {
            // The closing step is armed only after the last tap step: a Won reached any other way (dev jump, a test
            // shortcut) while tap 1 is still waiting must not mark the tour done.
            _rig.Runner.Start();

            _rig.Resolve(GameOutcome.Won);

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False);
        }

        [Test]
        public void Runner_FocusFlag_TrueDuringTour_FalseAfterCompleteAndStop()
        {
            _rig.Runner.Start();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True);

            _rig.PlayBothTaps();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True, "waiting for the win is still the tour");

            _rig.Resolve(GameOutcome.Won);
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.False, "completed tour releases the HUD");

            _rig.Reload(); // the same level again: the tour is done, no focus
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.False);
        }

        [Test]
        public void Runner_StopMidTour_ReleasesFocus()
        {
            _rig.Runner.Start();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True);

            _rig.Runner.Dispose();

            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.False);
            Assert.That(_rig.Gate.IsBlocked, Is.False, "a stopped tour leaves no block behind");
        }

        [Test]
        public void Runner_NoWinningOrder_PlaysNoTour()
        {
            using var rig = new TourRig(TourRig.TwoBoxLevel(new int[0]));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("no winningOrder"));

            rig.Runner.Start();

            Assert.That(rig.Interaction.IsTourFocused.Value, Is.False);
            Assert.That(rig.Gate.IsBlocked, Is.False);
            Assert.That(rig.View.Taps, Is.Empty);
        }
    }
}
