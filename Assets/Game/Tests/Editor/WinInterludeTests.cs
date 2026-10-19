using System;
using Gameplay.PlayScreen;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>TASK-613 plan §8 T7: the win interlude seam — one-shot take, last-wins arm, and a superseded owner's
    /// late dispose never disarms the newer number.</summary>
    [TestFixture]
    public sealed class WinInterludeTests
    {
        private static readonly Func<Observable<Unit>> A = () => Observable.Return(Unit.Default);
        private static readonly Func<Observable<Unit>> B = () => Observable.Return(Unit.Default);

        [Test]
        public void TryTake_Empty_IsFalse()
        {
            Assert.That(new WinInterlude().TryTake(out _), Is.False);
        }

        [Test]
        public void TryTake_IsOneShot()
        {
            var interlude = new WinInterlude();
            interlude.Arm(A);

            Assert.That(interlude.TryTake(out var play), Is.True);
            Assert.That(play, Is.SameAs(A));
            Assert.That(interlude.TryTake(out _), Is.False, "a number plays once");
        }

        [Test]
        public void Arm_LastWins()
        {
            var interlude = new WinInterlude();
            interlude.Arm(A);
            interlude.Arm(B);

            interlude.TryTake(out var play);

            Assert.That(play, Is.SameAs(B));
        }

        [Test]
        public void Dispose_OfSupersededOwner_DoesNotDisarmTheNewOne()
        {
            var interlude = new WinInterlude();
            var old = interlude.Arm(A);
            interlude.Arm(B);

            old.Dispose();

            Assert.That(interlude.TryTake(out var play), Is.True);
            Assert.That(play, Is.SameAs(B));
        }

        [Test]
        public void Dispose_OfCurrentOwner_Disarms()
        {
            var interlude = new WinInterlude();
            var handle = interlude.Arm(A);

            handle.Dispose();
            handle.Dispose(); // idempotent

            Assert.That(interlude.TryTake(out _), Is.False);
        }
    }
}
