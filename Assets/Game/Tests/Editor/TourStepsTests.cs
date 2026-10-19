using Gameplay.Core;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-613 tour beats beyond the TASK-524 rest gate (<see cref="TutorialRestGateTests"/>): the "not there, here"
    /// answer to a miss, the between-taps pause carried in the cue, the kind the card slot is lit by, the closing
    /// <see cref="AwaitWinStep"/> (TASK-634, replaced the "Совпало" step), and the input decorator reporting a refused tap
    /// back to the gate.
    /// </summary>
    [TestFixture]
    public sealed class TourStepsTests
    {
        private TourRig _rig;

        [SetUp]
        public void SetUp() => _rig = new TourRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void MissTap_PulsesView_OnlyWhileAllowOnlyActive()
        {
            var sub = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe();
            var elsewhere = TourRig.Tap(new Vec2(5f, 5f));

            _rig.Gate.NotifyRejected(elsewhere); // field still settling: blocked, no finger to answer
            Assert.That(_rig.View.Misses, Is.EqualTo(0), "a tap during the fall is not a miss");

            _rig.Model.SetFieldAtRest(true);
            _rig.Gate.NotifyRejected(elsewhere);
            Assert.That(_rig.View.Misses, Is.EqualTo(1), "a tap elsewhere while the finger points must pulse it");

            _rig.Gate.NotifyRejected(TourRig.Tap(TourRig.BottomPos));
            Assert.That(_rig.View.Misses, Is.EqualTo(1), "a tap the whitelist admits is not a miss");

            sub.Dispose();
            _rig.Gate.NotifyRejected(elsewhere);
            Assert.That(_rig.View.Misses, Is.EqualTo(1), "after the beat no one listens for misses");
        }

        [Test]
        public void Gate_ReportsMiss_OnlyForAWhitelistRefusal_NotForABlockOrAnOpenGate()
        {
            var misses = 0;
            using var _ = _rig.Gate.Rejected.Subscribe(__ => misses++);
            var elsewhere = TourRig.Tap(new Vec2(5f, 5f));

            _rig.Gate.NotifyRejected(elsewhere); // no whitelist at all: nothing was refused
            Assert.That(misses, Is.EqualTo(0));

            using (_rig.Gate.AllowOnly(i => i.X == 0f))
            {
                using (_rig.Gate.Block())
                    _rig.Gate.NotifyRejected(elsewhere); // blocked (a fall): not the whitelist's refusal
                Assert.That(misses, Is.EqualTo(0));

                _rig.Gate.NotifyRejected(elsewhere);
                Assert.That(misses, Is.EqualTo(1));
            }
        }

        [Test]
        public void GatedInputSource_ReportsRefusedTapAsMiss()
        {
            var inner = new Subject<InputIntent>();
            var source = new TutorialGatedInputSource(new SubjectInput(inner), _rig.Gate);
            var passed = 0;
            using var _ = source.Intents.Subscribe(__ => passed++);
            _rig.Model.SetFieldAtRest(true);
            using var step = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe();

            inner.OnNext(TourRig.Tap(new Vec2(5f, 5f)));
            inner.OnNext(TourRig.Tap(TourRig.BottomPos));

            Assert.That(_rig.View.Misses, Is.EqualTo(1));
            Assert.That(passed, Is.EqualTo(1), "the right tap still reaches the domain");
        }

        [Test]
        public void FirstTap_HasNoDelay_SecondTap_CarriesBetweenTapsDelay()
        {
            _rig.Model.SetFieldAtRest(true);
            using (new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe())
            using (new TapPieceStep(1, 2).Enter(_rig.Context()).Subscribe())
            {
                Assert.That(_rig.View.Taps[0].Delay, Is.EqualTo(0f));
                Assert.That(_rig.View.Taps[1].Delay, Is.EqualTo(_rig.Config.BetweenTapsDelay));
                Assert.That(_rig.Config.BetweenTapsDelay, Is.GreaterThan(0f), "the test must be able to tell them apart");
            }
        }

        [Test]
        public void TapCue_CarriesThePieceKind_ForTheCardSlot()
        {
            _rig.Model.SetFieldAtRest(true);
            using var _ = new TapPieceStep(1, 2).Enter(_rig.Context()).Subscribe();

            Assert.That(_rig.View.Taps[0].Kind, Is.EqualTo("box"));
            Assert.That(_rig.View.Taps[0].Origin, Is.EqualTo(TourRig.TopPos));
        }

        [Test]
        public void TapStep_OnPieceAlreadyReleased_CompletesAtOnce_WithoutShowing()
        {
            _rig.Model.SetFieldAtRest(true);
            _rig.Release(0);
            var completed = 0;

            using var _ = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe(__ => completed++);

            Assert.That(completed, Is.EqualTo(1));
            Assert.That(_rig.View.Taps, Is.Empty);
        }

        [Test]
        public void SilentTapStep_ShowsNothingOnArm_FirstMissBringsTheBeat_ReleaseCompletes()
        {
            _rig.Model.SetFieldAtRest(true);
            var completed = 0;
            using var _ = new TapPieceStep(1, 2, silent: true).Enter(_rig.Context()).Subscribe(__ => completed++);

            Assert.That(_rig.View.Taps, Is.Empty, "silent: someone else points at the piece");
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.False, "still gated to its piece");

            _rig.Gate.NotifyRejected(TourRig.Tap(TourRig.BottomPos));
            Assert.That(_rig.View.Taps.Count, Is.EqualTo(1));
            Assert.That(_rig.View.Taps[0].Delay, Is.EqualTo(0f), "a miss answers at once, even on a later ordinal");
            Assert.That(_rig.View.Misses, Is.EqualTo(1));

            _rig.Release(1);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void WinStep_IgnoresNoneLostAndReleases_CompletesOnWon()
        {
            var completed = 0;
            using var _ = new AwaitWinStep().Enter(_rig.Context()).Subscribe(__ => completed++);
            Assert.That(completed, Is.EqualTo(0), "Outcome replays None on Subscribe — no false completion");

            _rig.Model.SetFieldAtRest(true);
            _rig.Release(0);
            Assert.That(completed, Is.EqualTo(0), "a release is not a win");

            _rig.Resolve(GameOutcome.Won);

            Assert.That(completed, Is.EqualTo(1));
            Assert.That(_rig.View.Taps, Is.Empty, "the step draws nothing — the win beat does");
        }

        [Test]
        public void WinStep_DoesNotCompleteOnLost()
        {
            var completed = 0;
            using var _ = new AwaitWinStep().Enter(_rig.Context()).Subscribe(__ => completed++);

            _rig.Resolve(GameOutcome.Lost);

            Assert.That(completed, Is.EqualTo(0));
        }

        [Test]
        public void WinStep_DisposedBeforeWon_StaysSilent()
        {
            var completed = 0;
            var sub = new AwaitWinStep().Enter(_rig.Context()).Subscribe(__ => completed++);

            sub.Dispose(); // Lost / restart: the runner's StopSequence
            _rig.Resolve(GameOutcome.Won);

            Assert.That(completed, Is.EqualTo(0));
        }

        private sealed class SubjectInput : IGameplayInputSource
        {
            public SubjectInput(Observable<InputIntent> intents) => Intents = intents;

            public Observable<InputIntent> Intents { get; }
        }
    }
}
