using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-566: <see cref="GameplayPause"/> is a holder COUNT. Two modal screens each hold a token; closing one must
    /// not resume the game under the other, and a token disposed twice must not release someone else's hold.
    /// Mutations these catch: counter → bool (first test red), non-idempotent Dispose (second test red).
    /// </summary>
    [TestFixture]
    public sealed class GameplayPauseTests
    {
        [Test]
        public void NoHolders_IsNotPaused()
        {
            Assert.That(new GameplayPause().IsPaused, Is.False);
        }

        [Test]
        public void Acquire_TwoHolders_ReleaseOne_StillPaused()
        {
            var pause = new GameplayPause();
            var a = pause.Acquire();
            var b = pause.Acquire();

            a.Dispose();
            Assert.That(pause.IsPaused, Is.True, "the second holder still keeps the game paused");

            b.Dispose();
            Assert.That(pause.IsPaused, Is.False);
        }

        [Test]
        public void Dispose_Twice_ReleasesOnce()
        {
            var pause = new GameplayPause();
            var a = pause.Acquire();
            var b = pause.Acquire();

            a.Dispose();
            a.Dispose();
            Assert.That(pause.IsPaused, Is.True, "a repeated Dispose must not release b's hold");

            b.Dispose();
            Assert.That(pause.IsPaused, Is.False);
        }
    }
}
