using Gameplay.Tutorial;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-613 plan §8 T1–T3: the tour is marked completed only AFTER the "Совпало" beat has played (it used to be
    /// marked on the last release, before the verdict — <c>TutorialRunner.CompleteTutorial</c> pre-TASK-613), a loss
    /// after the last tap replays the tour and disarms the beat, and the HUD focus flag follows the tour's life.
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
        public void Runner_MarksCompleted_OnlyAfterInterludePlayed()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False,
                "the last release is not the end of the tour — the \"Совпало\" beat is");
            Assert.That(_rig.Interlude.TryTake(out var play), Is.True, "the beat waits for Won in the interlude");

            using var number = play().Subscribe(); // the resolution flow on Won
            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False, "not while the beat is still playing");

            _rig.View.FinishMatch();

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.True);
        }

        [Test]
        public void Runner_Completion_DoesNotClearTheView_SoTheBeatFadesIntoTheWinScreen()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();
            Assert.That(_rig.Interlude.TryTake(out var play), Is.True);
            using var number = play().Subscribe();
            var clearsBefore = _rig.View.Clears;

            _rig.View.FinishMatch();

            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.True);
            Assert.That(_rig.View.Clears, Is.EqualTo(clearsBefore),
                "completion must not Clear the view: the \"Совпало\" dim is held until the win screen covers the field");
        }

        [Test]
        public void Runner_LostAfterLastTap_ReplaysTour_AndDisarmsInterlude()
        {
            _rig.Runner.Start();
            _rig.PlayBothTaps();

            _rig.Reload(); // Lost → "Try again" reloads the level in place
            _rig.Model.SetFieldAtRest(true);

            Assert.That(_rig.Interlude.TryTake(out _), Is.False, "a later Won must not play a stale beat");
            Assert.That(_rig.Progress.IsCompleted(TourRig.TourId), Is.False);
            Assert.That(_rig.View.Taps.Count, Is.EqualTo(3), "tap 1, tap 2, then tap 1 again after the reload");
            Assert.That(_rig.View.Taps[2].Origin, Is.EqualTo(TourRig.BottomPos));
        }

        [Test]
        public void Runner_FocusFlag_TrueDuringTour_FalseAfterCompleteAndStop()
        {
            _rig.Runner.Start();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True);

            _rig.PlayBothTaps();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True, "the \"Совпало\" beat is still the tour");

            _rig.Interlude.TryTake(out var play);
            using (play().Subscribe())
                _rig.View.FinishMatch();
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
