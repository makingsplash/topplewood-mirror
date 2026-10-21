using System.Reflection;
using Gameplay.Configs;
using NUnit.Framework;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-660 (folded into TASK-064): a partial remote <c>config_rewarded</c> must not throw on the boot step.
    /// <c>ConfigWithJson</c> replaces the asset with the payload, so every section the payload omits arrives null, and
    /// the override dereferenced <c>Config.Revives</c> unguarded — on the player that step runs without try/catch and
    /// the boot stopped on the loading screen. Driven through the REMOTE branch (local switch forced off).
    /// </summary>
    [TestFixture]
    public sealed class RewardedConfigRemoteTests
    {
        private ConfigSwitcherOverride _switcher;
        private RewardedConfig _config;

        [SetUp]
        public void SetUp()
        {
            _switcher = new ConfigSwitcherOverride();
            _config = ScriptableObject.CreateInstance<RewardedConfig>();

            var asset = new RewardedConfig.Data
            {
                Revives = new RewardedConfig.ReviveForRewardedData
                {
                    StartAfterLevel = 1, DelaysForPayers = "4,2;9,1", DelaysForNonPayers = "6,1"
                }
            };
            typeof(ConfigWithJson<RewardedConfig.Data>)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_config, asset);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            _switcher.Dispose();
        }

        [TestCase("{}")]
        [TestCase("{\"Inters\":{\"StartAfterLevel\":5}}")]
        [TestCase("{\"Revives\":{\"StartAfterLevel\":3}}")]
        public void PartialRemote_DoesNotThrow_AndEverySectionExists(string payload)
        {
            Assert.DoesNotThrow(() => _config.InitWithRemote(payload));

            Assert.That(_config.Inters, Is.Not.Null);
            Assert.That(_config.Revives, Is.Not.Null);
            Assert.That(_config.AdBoosters, Is.Not.Null);
            Assert.That(_config.AdHearts, Is.Not.Null);
            Assert.That(_config.Revives.ParsedDelaysForPayers[10], Is.EqualTo(0), "no delay strings → no steps");
        }

        [Test]
        public void EmptyRemote_PlaysTheAssetDelays()
        {
            _config.InitWithRemote("");

            Assert.That(_config.Revives.ParsedDelaysForPayers[1], Is.EqualTo(4));
            Assert.That(_config.Revives.ParsedDelaysForPayers[3], Is.EqualTo(9));
            Assert.That(_config.Revives.ParsedDelaysForNonPayers[1], Is.EqualTo(6));
        }

        [Test]
        public void SteppedValues_ElementWithoutLength_IsSkipped_NotIndexOutOfRange()
        {
            SteppedValuesList list = null;
            Assert.DoesNotThrow(() => list = new SteppedValuesList(1, "5;7,1"));
            Assert.That(list[1], Is.EqualTo(7), "\"5\" has no length — skipped; the next valid step starts at startStep");
        }

        [Test]
        public void SteppedValues_NullString_HasNoSteps()
        {
            var list = new SteppedValuesList(1, null);
            Assert.That(list[5], Is.EqualTo(0));
        }
    }
}
