using Gameplay.Core;
using NUnit.Framework;
using Services.Gameplay;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-635 plan §5 T1: the Hint unlock rule table, row by row. The tour level sits at line-up index 3 (diamond_gem,
    /// the 4th place); <c>CampaignProgress.LevelNumber</c> is 1-based, so index i = <c>AtLineup(i)</c>.
    /// </summary>
    [TestFixture]
    public sealed class HintUnlockRulesTests
    {
        private const int TourIndex = 3;

        private static HintUnlockAction Decide(bool unlocked, bool completed, bool onTour, CampaignProgress p,
            int tourIndex = TourIndex) =>
            HintUnlockRules.Decide(unlocked, completed, onTour, tourIndex, p);

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FreshInstall_BeforeTheTourLevel_StaysLocked(int index)
        {
            Assert.That(Decide(false, false, false, CampaignCursor.AtLineup(index)), Is.EqualTo(HintUnlockAction.None));
        }

        [Test]
        public void OnTheTourLevel_NotCompleted_UnlocksWithGrant()
        {
            Assert.That(Decide(false, false, true, CampaignCursor.AtLineup(TourIndex)),
                Is.EqualTo(HintUnlockAction.UnlockWithGrant));
        }

        [Test]
        public void TourReplayedAfterAKill_AlreadyUnlocked_GrantsAgain()
        {
            Assert.That(Decide(true, false, true, CampaignCursor.AtLineup(TourIndex)),
                Is.EqualTo(HintUnlockAction.UnlockWithGrant), "a tour killed mid-way tops up again on the replay");
        }

        [Test]
        public void OnTheTourLevel_TourCompleted_DoesNothing()
        {
            Assert.That(Decide(true, true, true, CampaignCursor.AtLineup(TourIndex)), Is.EqualTo(HintUnlockAction.None));
        }

        [Test]
        public void LegacyPlayer_PastThePlace_UnlocksSilently()
        {
            Assert.That(Decide(false, false, false, CampaignCursor.AtLineup(TourIndex + 1)),
                Is.EqualTo(HintUnlockAction.UnlockSilent));
        }

        [Test]
        public void PoolReplayOfTheTourLevel_IsSilent_NotTheTour()
        {
            // Line-up cleared, the pool has served diamond_gem again: rule 2 must win over rule 3.
            var pool = new CampaignProgress(90, 80, 4);
            Assert.That(Decide(false, false, true, pool), Is.EqualTo(HintUnlockAction.UnlockSilent));
        }

        [Test]
        public void UnlockedWithoutTheTour_ThenPast_MarksSilently()
        {
            // DisableTutorial / a tour that never finished: unlocked on the level, then moved on — the tour gets marked.
            Assert.That(Decide(true, false, false, CampaignCursor.AtLineup(TourIndex + 1)),
                Is.EqualTo(HintUnlockAction.UnlockSilent));
        }

        [Test]
        public void TourLevelMissingFromTheLineup_UnlocksSilently_Once()
        {
            Assert.That(Decide(false, false, false, CampaignCursor.AtLineup(0), tourIndex: -1),
                Is.EqualTo(HintUnlockAction.UnlockSilent), "the button must not stay hidden because of data");
            Assert.That(Decide(true, false, false, CampaignCursor.AtLineup(0), tourIndex: -1),
                Is.EqualTo(HintUnlockAction.None));
        }

        [Test]
        public void Passed_IsPositional()
        {
            Assert.That(HintUnlockRules.Passed(CampaignCursor.AtLineup(TourIndex), TourIndex), Is.False);
            Assert.That(HintUnlockRules.Passed(CampaignCursor.AtLineup(TourIndex + 1), TourIndex), Is.True);
            Assert.That(HintUnlockRules.Passed(new CampaignProgress(1, 1, 0), TourIndex), Is.True, "line-up cleared");
        }

        [TestCase(0, 2, 2)]
        [TestCase(1, 2, 1)]
        [TestCase(2, 2, 0)]
        [TestCase(3, 2, 0)]
        public void TopUp_IsToN_NeverPlusN_NeverATakeAway(int current, int target, int expected)
        {
            Assert.That(HintUnlockRules.TopUpAmount(current, target), Is.EqualTo(expected));
        }
    }
}
