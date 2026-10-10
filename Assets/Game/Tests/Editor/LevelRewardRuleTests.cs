using System.Collections.Generic;
using Gameplay.Configs;
using Gameplay.Core;
using Newtonsoft.Json;
using NUnit.Framework;
using Services.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-432: the level-reward rule pays the full tier reward on EVERY win — winning the same level again pays the
    /// same as the first time (the TASK-240 first-clear gate is removed by owner decision). Drives the real
    /// <see cref="GameModel"/> through load→win twice and reads <see cref="LevelRewardService.RewardFor"/>; the wallet
    /// credit itself is not driven here (it writes the machine's PlayerPrefs, shared across every tree of the project).
    /// </summary>
    [TestFixture]
    public sealed class LevelRewardRuleTests
    {
        private const string LevelId = "replay_probe";
        private const string CoreConfigPath = "Assets/Game/Configs/CoreConfig.asset";

        private CoreConfig _core;
        private GameModel _model;

        [SetUp]
        public void SetUp()
        {
            // A clone of the project asset, not CreateInstance: the local-config path (ConfigSwitcher, on in batch)
            // reads the serialized data, which a bare CreateInstance leaves null. Remote path parses the code
            // defaults. Either way the reward table is parsed, so expectations are read from the config itself.
            _core = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CoreConfig>(CoreConfigPath));
            _core.InitWithRemote(JsonConvert.SerializeObject(new CoreConfig.Data()));
            _model = new GameModel();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_core);
        }

        private static LevelData Level(int tier) => new()
        {
            LevelId = LevelId,
            DifficultyTier = tier,
            Pieces = new List<PiecePlacement>(),
        };

        private int Play(LevelData level, GameOutcome outcome)
        {
            _model.LoadLevel(level);
            _model.Resolve(outcome);
            return LevelRewardService.RewardFor(_model, _core);
        }

        [Test]
        public void RepeatWinOfSameLevel_PaysFullTierReward()
        {
            var level = Level(5);
            var full = _core.GetLevelReward(5);
            Assume.That(full, Is.GreaterThan(0), "reward table must pay something for the probe to mean anything");

            Assert.That(Play(level, GameOutcome.Won), Is.EqualTo(full), "first win");
            Assert.That(Play(level, GameOutcome.Won), Is.EqualTo(full), "replay of the same level pays the full reward");
            Assert.That(Play(level, GameOutcome.Won), Is.EqualTo(full), "and keeps paying");
        }

        [Test]
        public void WinAfterLossOfSameLevel_PaysFullTierReward()
        {
            var level = Level(1);
            Assert.That(Play(level, GameOutcome.Won), Is.EqualTo(_core.GetLevelReward(1)));
            Assert.That(Play(level, GameOutcome.Lost), Is.EqualTo(0), "a loss pays nothing");
            Assert.That(Play(level, GameOutcome.Won), Is.EqualTo(_core.GetLevelReward(1)));
        }

        [Test]
        public void ReplayPaysByTier_NotAFlatReplayValue()
        {
            Play(Level(1), GameOutcome.Won);
            Play(Level(9), GameOutcome.Won);

            Assert.That(Play(Level(1), GameOutcome.Won), Is.EqualTo(_core.GetLevelReward(1)));
            Assert.That(Play(Level(9), GameOutcome.Won), Is.EqualTo(_core.GetLevelReward(9)));
        }
    }
}
