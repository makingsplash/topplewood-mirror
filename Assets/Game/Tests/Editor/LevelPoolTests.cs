using System.Collections.Generic;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-528: <see cref="LevelPool"/> never throws and never dead-ends — unknown and duplicate ids are skipped and
    /// named in <see cref="LevelPool.Problems"/>; a missing, broken or empty file falls back to the whole line-up.
    /// </summary>
    [TestFixture]
    public sealed class LevelPoolTests
    {
        private CampaignCursorTests.FakeLineup _lineup;

        [SetUp]
        public void SetUp() => _lineup = new CampaignCursorTests.FakeLineup(4);

        private LevelPool FromFile(string json) => new(new FakeStore(json), _lineup);

        [Test]
        public void IdsMapToLineupIndicesInPoolOrder()
        {
            var pool = FromFile("{\"schemaVersion\":1,\"pool\":[\"L2\",\"L0\",\"L3\"]}");

            Assert.AreEqual(3, pool.Count);
            Assert.IsFalse(pool.IsFallback);
            CollectionAssert.IsEmpty(pool.Problems);
            Assert.AreEqual(2, pool.LineupIndexAt(0));
            Assert.AreEqual(0, pool.LineupIndexAt(1));
            Assert.AreEqual(3, pool.LineupIndexAt(2));
        }

        [Test]
        public void UnknownAndDuplicateIds_AreSkippedAndNamed()
        {
            var pool = LevelPool.FromIds(new[] { "L1", "ghost", "L1", "L2" }, _lineup);

            Assert.AreEqual(2, pool.Count);
            Assert.IsFalse(pool.IsFallback);
            Assert.AreEqual(2, pool.Problems.Count);
            StringAssert.Contains("ghost", pool.Problems[0]);
            StringAssert.Contains("duplicate", pool.Problems[1]);
        }

        [Test]
        public void MissingFile_FallsBackToWholeLineup()
        {
            var pool = FromFile(null);

            Assert.IsTrue(pool.IsFallback);
            Assert.AreEqual(_lineup.Count, pool.Count);
            Assert.AreEqual(1, pool.Problems.Count);
            for (var i = 0; i < _lineup.Count; i++)
                Assert.AreEqual(i, pool.LineupIndexAt(i));
        }

        [Test]
        public void UnparsableFile_FallsBack()
        {
            var pool = FromFile("{ not json");

            Assert.IsTrue(pool.IsFallback);
            Assert.AreEqual(_lineup.Count, pool.Count);
            CollectionAssert.IsNotEmpty(pool.Problems);
        }

        [Test]
        public void AllIdsUnknown_FallsBack()
        {
            var pool = LevelPool.FromIds(new[] { "ghost" }, _lineup);

            Assert.IsTrue(pool.IsFallback);
            Assert.AreEqual(_lineup.Count, pool.Count);
            Assert.AreEqual(2, pool.Problems.Count, "the unknown id and the fallback itself are both named");
        }

        [Test]
        public void OutOfRangeCursor_DoesNotThrow()
        {
            var pool = LevelPool.FromIds(new[] { "L2", "L0" }, _lineup);

            Assert.AreEqual(0, pool.LineupIndexAt(-1), "−1 wraps to the last place");
            Assert.AreEqual(2, pool.LineupIndexAt(pool.Count));
            Assert.AreEqual(0, pool.LineupIndexAt(-3));
        }

        private sealed class FakeStore : ILevelStore
        {
            private readonly string _text;

            public FakeStore(string text) => _text = text;

            public string ReadText(string name) => name == LevelPool.PoolFileName ? _text : null;
        }
    }
}
