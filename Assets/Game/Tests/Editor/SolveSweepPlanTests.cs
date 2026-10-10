using System.Collections.Generic;
using System.Linq;
using Gameplay.Core.Sim;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-427 scene-free tests for <see cref="SolveSweepPlan.PinnedOrder"/>: above the full-permutation threshold the
    /// sweep samples 60 seeded orders, which cannot prove a level whose winning set is narrower than ~1/60 — so the
    /// level's authored winningOrder is replayed as its own bucket. Pinned only when it is replayable as a release
    /// order (a full permutation of the tappable pieces) and only when the level is NOT already enumerated.
    /// </summary>
    [TestFixture]
    public sealed class SolveSweepPlanTests
    {
        private const int MaxFullPermute = 5;

        // Six tappable pieces behind one preplaced piece at index 0 — the lighthouse shape: placement indices 1..6.
        private static readonly int[] SixTappable = { 1, 2, 3, 4, 5, 6 };

        [Test]
        public void SampledLevel_FullPermutation_IsPinnedVerbatim()
        {
            var winning = new[] { 2, 1, 3, 6, 5, 4 };
            var pinned = SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, winning, null);
            Assert.That(pinned, Is.EqualTo(winning), "the authored order is replayed as written, in placement indices");
            Assert.That(pinned, Is.Not.SameAs(winning), "a copy — the sweep must not alias level data");
        }

        [Test]
        public void SampledLevel_PinIsAnnouncedInTheLog()
        {
            var log = new List<string>();
            SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, new[] { 1, 2, 3, 4, 5, 6 }, log.Add);
            Assert.That(log.Single(), Does.Contain(SolveSweepPlan.PinnedPolicyId).And.Contain("1-2-3-4-5-6"));
        }

        [Test]
        public void EnumeratedLevel_IsNotPinned_ThePermutationsAlreadyContainIt()
            => Assert.That(SolveSweepPlan.PinnedOrder(new[] { 0, 1, 2, 3, 4 }, MaxFullPermute,
                new[] { 4, 3, 2, 1, 0 }, null), Is.Null);

        [Test]
        public void NoWinningOrder_IsNotPinned()
            => Assert.That(SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, null, null), Is.Null);

        [Test]
        public void PartialOrder_IsNotPinned_AndTheReasonIsLogged()
        {
            var log = new List<string>();
            var pinned = SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, new[] { 1, 2, 3 }, log.Add);
            Assert.That(pinned, Is.Null, "a partial order cannot be replayed as a release order");
            Assert.That(log.Single(), Does.Contain("NOT pinned").And.Contain("covers 3 of 6"));
        }

        [Test]
        public void OrderNamingAPreplacedPiece_IsNotPinned()
        {
            var log = new List<string>();
            var pinned = SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, new[] { 0, 1, 2, 3, 4, 5 }, log.Add);
            Assert.That(pinned, Is.Null, "index 0 is preplaced — never tap-released");
            Assert.That(log.Single(), Does.Contain("not a permutation"));
        }

        [Test]
        public void DuplicateIndex_IsNotPinned()
            => Assert.That(SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute,
                new[] { 1, 1, 2, 3, 4, 5 }, null), Is.Null);

        [Test]
        public void Pinning_LeavesTheSeededSampleUntouched()
        {
            // The pin is a separate bucket: the random sample (and so the loseability audit and every existing
            // poseHash of a >5-piece level) is byte-for-byte what BuildOrders produced before TASK-427.
            var before = SolveSweepPlan.BuildOrders(SixTappable, MaxFullPermute, 60, 12345, null);
            SolveSweepPlan.PinnedOrder(SixTappable, MaxFullPermute, new[] { 1, 2, 3, 4, 5, 6 }, null);
            var after = SolveSweepPlan.BuildOrders(SixTappable, MaxFullPermute, 60, 12345, null);
            Assert.That(after.Select(o => string.Join("-", o)), Is.EqualTo(before.Select(o => string.Join("-", o))));
        }
    }
}
