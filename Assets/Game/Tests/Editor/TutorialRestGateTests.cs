using Gameplay.Core;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-524: a tour beat shows and admits the next piece only once the field is at rest
    /// (<see cref="GameModel.FieldAtRest"/>), not on the previous piece's release — following the finger without a
    /// pause used to drop the sail onto a hull still in the air and lose level 1. Moved in TASK-613 from the composite
    /// <c>AssembleInOrderStep</c> onto the per-piece <see cref="TapPieceStep"/> chain the runner builds from the level's
    /// winningOrder: the cross-piece assertion runs through the real <see cref="TutorialRunner"/>, the per-beat ones on
    /// the step itself. Pure model + real <see cref="TutorialInputGate"/> (<see cref="TourRig"/>).
    /// </summary>
    [TestFixture]
    public sealed class TutorialRestGateTests
    {
        private TourRig _rig;

        [SetUp]
        public void SetUp() => _rig = new TourRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void Release_DropsFieldAtRest_BeforeReleaseObserversRun()
        {
            _rig.Model.SetFieldAtRest(true);
            var seenAtRelease = true;
            using var _ = _rig.Model.PieceReleased.Subscribe(__ => seenAtRelease = _rig.Model.FieldAtRest.CurrentValue);

            _rig.Model.RegisterAttempt(_rig.Model.Pieces[0].InstanceId);

            Assert.That(seenAtRelease, Is.False);
        }

        [Test]
        public void LoadLevel_ResetsFieldAtRest()
        {
            _rig.Model.SetFieldAtRest(true);

            _rig.Reload();

            Assert.That(_rig.Model.FieldAtRest.CurrentValue, Is.False);
        }

        [Test]
        public void Tour_NextTargetNotArmedUntilFieldAtRest()
        {
            _rig.Model.SetFieldAtRest(true);
            _rig.Runner.Start();

            Assert.That(_rig.View.Taps.ConvertAll(t => t.Origin), Is.EqualTo(new[] { TourRig.BottomPos }));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.True);
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.TopPos)), Is.False);

            _rig.Release(0); // the bottom piece starts falling

            // Falling: the finger is gone and NO tap passes — not the top piece, not anything else.
            Assert.That(_rig.View.Taps.Count, Is.EqualTo(1), "the next piece must not be highlighted mid-fall");
            Assert.That(_rig.View.Hides, Is.GreaterThan(0));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.TopPos)), Is.False, "the next piece must not be tappable mid-fall");
            Assert.That(_rig.Gate.Allows(TourRig.Tap(new Vec2(5f, 5f))), Is.False, "the gate must not open to everything mid-fall");

            _rig.Model.SetFieldAtRest(true); // the bottom piece landed, the field is quiet

            Assert.That(_rig.View.Taps.ConvertAll(t => t.Origin), Is.EqualTo(new[] { TourRig.BottomPos, TourRig.TopPos }));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.TopPos)), Is.True);
        }

        [Test]
        public void TapStep_WaitsForInitialRest()
        {
            using var sub = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe();

            Assert.That(_rig.View.Taps, Is.Empty);
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.False);

            _rig.Model.SetFieldAtRest(true);

            Assert.That(_rig.View.Taps.ConvertAll(t => t.Origin), Is.EqualTo(new[] { TourRig.BottomPos }));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.True);
        }

        [Test]
        public void TapStep_CompletesOnRelease_AndDisposeReleasesTheGate()
        {
            _rig.Model.SetFieldAtRest(true);
            var completed = 0;
            var sub = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe(_ => completed++);

            Assert.That(completed, Is.EqualTo(0));
            _rig.Release(0);

            Assert.That(completed, Is.EqualTo(1), "one emission, on the piece's real release");
            sub.Dispose();
            Assert.That(_rig.Gate.IsBlocked, Is.False);
            Assert.That(_rig.Gate.Allows(TourRig.Tap(new Vec2(5f, 5f))), Is.True);
        }

        [Test]
        public void TapStep_DisposeMidWait_ReleasesTheBlock()
        {
            var sub = new TapPieceStep(0, 1).Enter(_rig.Context()).Subscribe();
            Assert.That(_rig.Gate.IsBlocked, Is.True);

            sub.Dispose(); // LOSS-reload disposes without Exit (TutorialRunner.StopSequence)

            Assert.That(_rig.Gate.IsBlocked, Is.False);
        }
    }
}
