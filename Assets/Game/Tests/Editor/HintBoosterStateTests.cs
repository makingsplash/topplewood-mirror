using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.PlayScreen;
using Gameplay.Tutorial;
using NUnit.Framework;
using Services.Gameplay;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-638: the Hint booster's state against its store — the owner's phone case. A DEV "Reset Progress" rewrote the
    /// prefs (locked, 0 charges, tour not completed) and reloaded the level IN PLACE; the service kept its in-memory
    /// "unlocked, 3" and the unlock controller its session cache, so on the tour level nothing was granted, the badge read
    /// "3" and every tap on the button died in <c>TrySpendActivation</c>. Plus the owner's numbers: exactly 2 charges on
    /// the tour (an older build's leftover 3 must not survive it) and no daily refill at all.
    /// </summary>
    [TestFixture]
    public sealed class HintBoosterStateTests
    {
        private const int TourIndex = 3;

        [Test]
        public void TourGrant_IsExactlyTheGrant_NotATopUpOverALeftover()
        {
            var store = new MemoryStore { Charges = 3 }; // an older build's install seed
            var hint = new HintBoosterService(store);

            hint.Unlock(introGrant: true);

            Assert.That(store.Charges, Is.EqualTo(2), "the owner's word is 2 on the tour, not max(3, 2)");
            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(2));
            Assert.That(store.Unlocked && hint.Unlocked.CurrentValue, Is.True);
        }

        [Test]
        public void TourReplayedAfterASpend_SetsTheGrantAgain()
        {
            var store = new MemoryStore { Charges = 1, Unlocked = true };
            var hint = new HintBoosterService(store);

            hint.Unlock(introGrant: true);

            Assert.That(store.Charges, Is.EqualTo(2));
        }

        [Test]
        public void SilentUnlock_LeavesTheChargesAlone()
        {
            var store = new MemoryStore { Charges = 3 };
            var hint = new HintBoosterService(store);

            hint.Unlock(introGrant: false);

            Assert.That(store.Charges, Is.EqualTo(3));
            Assert.That(store.Unlocked, Is.True);
        }

        [Test]
        public void NoDailyRefill_AnUnlockedEmptyStockStaysEmpty()
        {
            var store = new MemoryStore { Charges = 0, Unlocked = true };
            var hint = new HintBoosterService(store);
            hint.ResyncFromStore();

            Assert.That(store.Charges, Is.EqualTo(0), "the owner: no daily top-ups of the booster, ever");
            Assert.That(store.Grants, Is.EqualTo(0));
            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(0));
        }

        [Test]
        public void ResyncFromStore_DropsAMirrorWrittenBehindTheServicesBack()
        {
            var store = new MemoryStore { Charges = 3, Unlocked = true };
            var hint = new HintBoosterService(store);

            store.Charges = 0; // the DEV reset writes the prefs, not the service
            store.Unlocked = false;
            hint.ResyncFromStore();

            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(0));
            Assert.That(hint.Unlocked.CurrentValue, Is.False);
        }

        [Test]
        public void FailedSpend_ShowsTheStoresTruth()
        {
            var store = new MemoryStore { Charges = 3, Unlocked = true };
            var hint = new HintBoosterService(store);
            store.Charges = 0;

            Assert.That(hint.TrySpendActivation(), Is.False);
            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(0), "a badge over an empty store must not keep offering a tap");
        }

        [Test]
        public void DevResetInPlace_ThenTheTourLevel_GrantsTwo_TheOwnersPhone()
        {
            var levels = new Lineup(6);
            var model = new GameModel();
            var campaign = new Campaign { Progress = CampaignCursor.AtLineup(5) };
            var progress = new TourRig.MemoryProgress();
            progress.MarkCompleted(BoosterTourRecipe.HintTourId);
            var store = new MemoryStore { Charges = 3, Unlocked = true }; // past the tour, old seed left over
            var hint = new HintBoosterService(store);
            var catalog = new TutorialCatalog(new Dictionary<string, ITourRecipe>
            {
                [Lineup.IdOf(TourIndex)] = new BoosterTourRecipe(BoosterTourRecipe.HintTourId, BoosterTourRecipe.HintButtonTargetId),
            });
            model.LoadLevel(levels.Load(5));
            var controller = new HintUnlockController(model, hint, campaign, levels, catalog, progress);
            controller.Start();

            // DEV "Reset Progress": prefs → a fresh install, then the bridge reloads the level in place (same scope).
            store.Charges = 0;
            store.Unlocked = false;
            progress.Reset(BoosterTourRecipe.HintTourId);
            campaign.Progress = CampaignCursor.AtLineup(0);
            model.LoadLevel(levels.Load(0));

            Assert.That(hint.Unlocked.CurrentValue, Is.False, "level 1 after a reset: the button is hidden");
            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(0));

            campaign.Progress = CampaignCursor.AtLineup(TourIndex);
            model.LoadLevel(levels.Load(TourIndex));

            Assert.That(hint.Unlocked.CurrentValue, Is.True);
            Assert.That(store.Charges, Is.EqualTo(2), "the tour level grants on the reloaded session too");
            Assert.That(hint.Charges.CurrentValue, Is.EqualTo(2), "the badge reads the store, not a stale 3");
            Assert.That(hint.TrySpendActivation(), Is.True, "the tour's tap can spend");

            controller.Dispose();
        }

        private sealed class MemoryStore : IBoosterChargeStore
        {
            public int Charges;
            public bool Unlocked;
            public int Grants;

            public int Get(BoosterId id) => Charges;

            public bool TrySpend(BoosterId id, int amount)
            {
                if (amount <= 0 || Charges < amount)
                    return false;
                Charges -= amount;
                return true;
            }

            public void Grant(BoosterId id, int amount, ChargeSource source, int cap)
            {
                Grants++;
                if (amount > 0)
                    Charges = System.Math.Min(Charges + amount, cap);
            }

            public void Set(BoosterId id, int amount, ChargeSource source) => Charges = System.Math.Max(0, amount);

            public bool IsUnlocked(BoosterId id) => Unlocked;

            public void MarkUnlocked(BoosterId id) => Unlocked = true;
        }

        private sealed class Campaign : ICampaignStore
        {
            public CampaignProgress Progress;

            public CampaignProgress Read() => Progress;

            public void Write(CampaignProgress progress) => Progress = progress;
        }

        /// <summary>A line-up of distinct two-box levels with ids "lvl_0".."lvl_n-1".</summary>
        private sealed class Lineup : ILevelSource
        {
            private readonly List<LevelData> _levels = new();

            public Lineup(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    var level = TourRig.TwoBoxLevel();
                    level.LevelId = IdOf(i);
                    _levels.Add(level);
                }
            }

            public static string IdOf(int index) => $"lvl_{index}";

            public int Count => _levels.Count;

            public LevelData Load(int index) => _levels[index];
        }
    }
}
