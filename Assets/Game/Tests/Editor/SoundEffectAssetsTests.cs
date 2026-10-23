using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SGS.Configs;
using SGS.Services.Audio;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine.Audio;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-667 plan §8 T3: the game's sound data. Exactly the eight design sounds exist as SoundEffect assets, each is
    /// routed through the «Sounds» group of the AudioConfig mixer (a source without it bypasses the mixer, and the
    /// settings toggle would silence only part of the game), the voice limits hold, the generated Sounds constants match
    /// the assets (no tilesort leftovers), and the folder is addressable as «SoundEffects» — the key AudioService loads.
    /// </summary>
    [TestFixture]
    public sealed class SoundEffectAssetsTests
    {
        private const string Folder = "Assets/Game/Audio/SoundEffects";
        private const string AudioConfigPath = "Assets/Game/Configs/AudioConfig.asset";

        private static readonly string[] DesignSounds =
            { "Button", "Hint", "Lose", "PieceLand", "PieceRelease", "WinFrame", "WinLift", "WinStar" };

        private static Dictionary<string, SoundEffect> LoadEffects() =>
            AssetDatabase.FindAssets("t:SoundEffect", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SoundEffect>)
                .Where(e => e != null)
                .ToDictionary(e => e.name);

        [Test]
        public void ExactlyTheDesignSounds_Exist()
        {
            Assert.That(LoadEffects().Keys.OrderBy(k => k), Is.EqualTo(DesignSounds));
        }

        [Test]
        public void EverySound_GoesThroughTheSoundsGroupOfTheConfigMixer()
        {
            var config = AssetDatabase.LoadAssetAtPath<AudioConfig>(AudioConfigPath);
            Assert.That(config, Is.Not.Null, AudioConfigPath);
            Assert.That(config.MasterMixer, Is.Not.Null, "AudioConfig has no mixer");
            foreach (var effect in LoadEffects().Values)
            {
                var group = new SerializedObject(effect).FindProperty("_mixer").objectReferenceValue as AudioMixerGroup;
                Assert.That(group, Is.Not.Null, $"{effect.name}: no mixer group — the toggle would not silence it");
                Assert.That(group.name, Is.EqualTo("Sounds"), effect.name);
                Assert.That(group.audioMixer, Is.EqualTo(config.MasterMixer), effect.name);
            }
        }

        [Test]
        public void VoiceLimits_MatchTheDesign()
        {
            var effects = LoadEffects();
            Assert.That(CountMax(effects["PieceLand"]), Is.EqualTo(3));
            Assert.That(CountMax(effects["PieceRelease"]), Is.EqualTo(2));
        }

        [Test]
        public void EverySound_HasAtLeastOneClip()
        {
            foreach (var effect in LoadEffects().Values)
                Assert.That(new SerializedObject(effect).FindProperty("_clips").arraySize, Is.GreaterThan(0), effect.name);
        }

        [Test]
        public void SoundsConstants_AreExactlyTheAssets()
        {
            var names = typeof(Scripts.Audio.Sounds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue())
                .OrderBy(n => n);
            Assert.That(names, Is.EqualTo(DesignSounds));
        }

        [Test]
        public void Folder_IsAddressable_AsSoundEffects()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.That(settings, Is.Not.Null, "no Addressables settings");
            var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(Folder));
            Assert.That(entry, Is.Not.Null, $"{Folder} is not an Addressables entry");
            Assert.That(entry.address, Is.EqualTo("SoundEffects"),
                "AudioService loads SoundEffects/<name>.asset — the folder address must be SoundEffects");
        }

        private static int CountMax(SoundEffect effect) =>
            new SerializedObject(effect).FindProperty("_countMax").intValue;
    }
}
