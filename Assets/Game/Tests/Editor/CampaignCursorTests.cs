using System.Collections.Generic;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-528: the campaign rule table (architect 03 §3.3) row by row on a fake line-up of 5 and the pool
    /// <c>[3, 1]</c> — first pass (A), catch-up after the line-up grew (B1), the pool circle (B2), dev stepping.
    ///
    /// Proven by mutation (docs/platform/testing.md), the boundary is the A → pool transition in
    /// <see cref="CampaignCursor.Resolve"/>/<see cref="CampaignCursor.AfterWin"/> — results are in the implementer
    /// artifact of TASK-528 (04-dev-implementer.md §Мутации): <c>index == count − 1</c> → <c>count − 2</c> and → <c>count</c>,
    /// and the historical class "the last win wraps to Fresh" each turn <see cref="WinOnLastLineupLevel_EntersPool"/> red.
    /// </summary>
    [TestFixture]
    public sealed class CampaignCursorTests
    {
        private const int LineupCount = 5;

        private FakeLineup _lineup;
        private LevelPool _pool;

        [SetUp]
        public void SetUp()
        {
            _lineup = new FakeLineup(LineupCount);
            _pool = LevelPool.FromIds(new[] { "L3", "L1" }, _lineup);
        }

        private CampaignPosition Resolve(CampaignProgress p) => CampaignCursor.Resolve(p, LineupCount, _pool);

        private CampaignProgress Win(CampaignProgress p) => CampaignCursor.AfterWin(p, LineupCount, _pool);

        [Test]
        public void Fresh_LoadsFirstLineupLevel()
        {
            var position = Resolve(CampaignProgress.Fresh);

            Assert.AreEqual(0, position.LineupIndex);
            Assert.AreEqual(CampaignTrack.Lineup, position.Track);
            Assert.IsFalse(position.IsFinalLineupLevel);
        }

        [Test]
        public void WinInsideLineup_AdvancesNumberAndIndex()
        {
            var next = Win(new CampaignProgress(3, 0, 0));

            Assert.AreEqual(new CampaignProgress(4, 0, 0), next);
            Assert.AreEqual(3, Resolve(next).LineupIndex);
            Assert.AreEqual(CampaignTrack.Lineup, Resolve(next).Track);
        }

        [Test]
        public void WinOnLastLineupLevel_EntersPool()
        {
            var last = new CampaignProgress(LineupCount, 0, 0);
            Assert.IsTrue(Resolve(last).IsFinalLineupLevel, "the last line-up level shows ALL DONE!");

            var next = Win(last);

            Assert.AreEqual(new CampaignProgress(LineupCount + 1, LineupCount, 0), next,
                "number keeps growing, line-up cleared, pool from place 0 — no wrap to level 1");
            Assert.IsTrue(next.CampaignCompleted);
            var position = Resolve(next);
            Assert.AreEqual(CampaignTrack.Pool, position.Track);
            Assert.AreEqual(0, position.PoolPlace);
            Assert.AreEqual(3, position.LineupIndex, "pool place 0 is L3");
            Assert.IsFalse(position.IsFinalLineupLevel);
        }

        [Test]
        public void OnlyLastLineupLevelIsFinal()
        {
            for (var n = 1; n <= LineupCount; n++)
                Assert.AreEqual(n == LineupCount, Resolve(new CampaignProgress(n, 0, 0)).IsFinalLineupLevel, $"Level {n}");
        }

        [Test]
        public void PoolWin_MovesAroundTheCircle()
        {
            var place1 = new CampaignProgress(7, LineupCount, 1);
            Assert.AreEqual(1, Resolve(place1).LineupIndex, "pool place 1 is L1");

            var next = Win(place1);

            Assert.AreEqual(new CampaignProgress(8, LineupCount, 0), next, "the circle wraps, the number grows");
            Assert.AreEqual(3, Resolve(next).LineupIndex);
            Assert.IsFalse(Resolve(next).IsFinalLineupLevel, "the pool never shows ALL DONE!");
        }

        [Test]
        public void StaleCursor_IsTakenModuloThePool()
        {
            var position = Resolve(new CampaignProgress(40, LineupCount, 7));

            Assert.AreEqual(1, position.PoolPlace);
            Assert.AreEqual(1, position.LineupIndex);
        }

        [Test]
        public void LineupGrew_CatchUpPlaysNewLevelsThenReturnsToSavedPoolPlace()
        {
            var lineup7 = new FakeLineup(7);
            var pool = LevelPool.FromIds(new[] { "L3", "L1" }, lineup7);
            var p = new CampaignProgress(9, LineupCount, 1);   // was in the pool at place 1, two levels appended

            var a = CampaignCursor.Resolve(p, 7, pool);
            Assert.AreEqual(CampaignTrack.CatchUp, a.Track);
            Assert.AreEqual(5, a.LineupIndex);
            Assert.IsFalse(a.IsFinalLineupLevel, "catch-up never shows ALL DONE!");

            p = CampaignCursor.AfterWin(p, 7, pool);
            Assert.AreEqual(6, CampaignCursor.Resolve(p, 7, pool).LineupIndex);
            Assert.IsFalse(CampaignCursor.Resolve(p, 7, pool).IsFinalLineupLevel);

            p = CampaignCursor.AfterWin(p, 7, pool);
            Assert.AreEqual(new CampaignProgress(11, 7, 1), p, "catch-up keeps the saved pool place");
            var back = CampaignCursor.Resolve(p, 7, pool);
            Assert.AreEqual(CampaignTrack.Pool, back.Track);
            Assert.AreEqual(1, back.PoolPlace);
        }

        [Test]
        public void LineupShrank_StaysInPool()
        {
            var position = Resolve(new CampaignProgress(20, 9, 0));

            Assert.AreEqual(CampaignTrack.Pool, position.Track);
            Assert.AreEqual(3, position.LineupIndex);
        }

        [Test]
        public void NumberOutOfRangeOnFirstPass_ClampsAndStillMovesForward()
        {
            Assert.AreEqual(0, Resolve(new CampaignProgress(0, 0, 0)).LineupIndex);
            Assert.AreEqual(new CampaignProgress(2, 0, 0), Win(new CampaignProgress(0, 0, 0)), "never stalls on level 1");
            Assert.AreEqual(LineupCount - 1, Resolve(new CampaignProgress(99, 0, 0)).LineupIndex);
        }

        [Test]
        public void Step_OnLineupEdge_IsNoOp()
        {
            var first = new CampaignProgress(1, 0, 0);
            var last = new CampaignProgress(LineupCount, 0, 0);

            Assert.AreEqual(first, CampaignCursor.Step(first, -1, LineupCount, _pool));
            Assert.AreEqual(last, CampaignCursor.Step(last, 1, LineupCount, _pool), "no wrap, no entry into the pool");
            Assert.AreEqual(new CampaignProgress(3, 0, 0), CampaignCursor.Step(new CampaignProgress(2, 0, 0), 1, LineupCount, _pool));
        }

        [Test]
        public void Step_InPool_GoesAroundTheCircle()
        {
            var place0 = CampaignCursor.AtPool(0, LineupCount);
            Assert.AreEqual(new CampaignProgress(LineupCount + 1, LineupCount, 0), place0);

            var back = CampaignCursor.Step(place0, -1, LineupCount, _pool);

            Assert.AreEqual(1, Resolve(back).PoolPlace, "◀ from place 0 lands on the last place");
            Assert.AreEqual(LineupCount + 1, back.LevelNumber, "the number never drops below Count + 1");
            Assert.AreEqual(0, Resolve(CampaignCursor.Step(back, 1, LineupCount, _pool)).PoolPlace);
        }

        [Test]
        public void Step_InCatchUp_StaysWithinOneToCount()
        {
            var lineup7 = new FakeLineup(7);
            var pool = LevelPool.FromIds(new[] { "L3" }, lineup7);
            var catchUp = new CampaignProgress(9, 5, 0);

            Assert.AreEqual(new CampaignProgress(8, 4, 0), CampaignCursor.Step(catchUp, -1, 7, pool));
            var one = new CampaignProgress(5, 1, 0);
            Assert.AreEqual(one, CampaignCursor.Step(one, -1, 7, pool), "catch-up does not fall back into the first pass");
        }

        [Test]
        public void AtLineup_IsFirstPass()
        {
            Assert.AreEqual(new CampaignProgress(3, 0, 0), CampaignCursor.AtLineup(2));
        }

        internal sealed class FakeLineup : ILevelSource
        {
            private readonly List<LevelData> _levels = new();

            public FakeLineup(int count)
            {
                for (var i = 0; i < count; i++)
                    _levels.Add(new LevelData { LevelId = $"L{i}", Pieces = new List<PiecePlacement>() });
            }

            public int Count => _levels.Count;

            public LevelData Load(int index) => _levels[index < 0 ? 0 : index >= _levels.Count ? _levels.Count - 1 : index];
        }
    }
}
