using Gameplay.Configs;
using Newtonsoft.Json;
using NUnit.Framework;
using Services.Gameplay;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-658 plan §8.2: the between-levels interstitial rule (<see cref="InterstitialRules.ShouldOffer"/>) row by row,
    /// plus the config_ads JSON contract — a PARTIAL remote payload must keep the unsent fields at their C# defaults
    /// (the class of error that would have killed <c>config_rewarded</c>, plan §2).
    /// </summary>
    [TestFixture]
    public sealed class InterstitialRulesTests
    {
        private static AdsConfig.InterstitialData Cfg(bool enabled = true, int fromLevel = 10, bool onLose = false) =>
            new() { Enabled = enabled, FromLevel = fromLevel, OnLose = onLose };

        [Test]
        public void AfterWin_BelowThreshold_NoOffer()
        {
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterWin, 9, Cfg()), Is.False);
        }

        [Test]
        public void AfterWin_AtThreshold_Offers()
        {
            // "FromLevel = 10" = the first interstitial after finishing level 10 (transition 10 → 11).
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterWin, 10, Cfg()), Is.True);
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterWin, 57, Cfg()), Is.True);
        }

        [TestCase(BetweenLevelsMoment.AfterWin)]
        [TestCase(BetweenLevelsMoment.AfterLose)]
        public void Disabled_NeverOffers(BetweenLevelsMoment moment)
        {
            Assert.That(InterstitialRules.ShouldOffer(moment, 100, Cfg(enabled: false, onLose: true)), Is.False);
        }

        [Test]
        public void AfterLose_OnLoseOff_NoOffer()
        {
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterLose, 50, Cfg(onLose: false)), Is.False);
        }

        [Test]
        public void AfterLose_OnLoseOn_FollowsThreshold()
        {
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterLose, 9, Cfg(onLose: true)), Is.False);
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterLose, 10, Cfg(onLose: true)), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LowThreshold_OffersFromTheFirstLevel(int fromLevel)
        {
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterWin, 1, Cfg(fromLevel: fromLevel)), Is.True);
        }

        [Test]
        public void NullConfig_NoOffer()
        {
            Assert.That(InterstitialRules.ShouldOffer(BetweenLevelsMoment.AfterWin, 100, null), Is.False);
        }

        [Test]
        public void PartialRemoteJson_KeepsUnsentFieldsAtDefaults()
        {
            var data = JsonConvert.DeserializeObject<AdsConfig.Data>("{\"Interstitial\":{\"FromLevel\":3}}");

            Assert.That(data.Interstitial, Is.Not.Null);
            Assert.That(data.Interstitial.FromLevel, Is.EqualTo(3));
            Assert.That(data.Interstitial.Enabled, Is.True);
            Assert.That(data.Interstitial.OnLose, Is.False);
        }

        [Test]
        public void SayLauncherPayload_RoundTrips()
        {
            // The exact value handed to the owner for SayLauncher (plan §5), unescaped.
            var data = JsonConvert.DeserializeObject<AdsConfig.Data>(
                "{\"Interstitial\":{\"Enabled\":true,\"FromLevel\":10,\"OnLose\":false}}");

            Assert.That(data.Interstitial.Enabled, Is.True);
            Assert.That(data.Interstitial.FromLevel, Is.EqualTo(10));
            Assert.That(data.Interstitial.OnLose, Is.False);
        }

        [Test]
        public void EmptyObject_LeavesSectionNull_DefaultsComeFromTheGuard()
        {
            // "{}" yields a null section; AdsConfig.InitWithRemote's `??= new()` restores the C# defaults.
            var data = JsonConvert.DeserializeObject<AdsConfig.Data>("{}");
            Assert.That(data.Interstitial, Is.Null);

            var fallback = new AdsConfig.InterstitialData();
            Assert.That(fallback.Enabled, Is.True);
            Assert.That(fallback.FromLevel, Is.EqualTo(10));
            Assert.That(fallback.OnLose, Is.False);
        }
    }
}
