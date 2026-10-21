using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-064: a missing or unreadable remote section plays the ASSET's data, not a blank <c>new TData()</c> — the
    /// template's behaviour, under which every section of a freshly registered app ran on C# zero defaults and null
    /// nested objects threw on the player's boot step. And the remote is a whole blob: a valid payload REPLACES the
    /// asset, it is never merged over it (publisher contract). Driven through <c>InitWithRemote</c> with the editor's
    /// local switch forced off, i.e. the same entry the boot calls.
    /// </summary>
    [TestFixture]
    public sealed class ConfigWithJsonFallbackTests
    {
        public sealed class ProbeConfig : ConfigWithJson<ProbeConfig.Data>
        {
            [Serializable]
            public class Data
            {
                public int Value;
                public string Label;
            }

            public Data Current => Config;
        }

        private ConfigSwitcherOverride _switcher;
        private ProbeConfig _config;
        private ProbeConfig.Data _asset;

        [SetUp]
        public void SetUp()
        {
            _switcher = new ConfigSwitcherOverride();
            _config = ScriptableObject.CreateInstance<ProbeConfig>();
            _asset = new ProbeConfig.Data { Value = 7, Label = "asset" };
            SetAssetData(_config, _asset);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            _switcher.Dispose();
        }

        [Test]
        public void EmptyRemote_PlaysTheAsset_NotABlankObject()
        {
            _config.InitWithRemote("");

            Assert.That(_config.Current, Is.SameAs(_asset), "an absent section must play the asset, not new TData()");
        }

        [Test]
        public void NullRemote_PlaysTheAsset()
        {
            _config.InitWithRemote(null);

            Assert.That(_config.Current, Is.SameAs(_asset));
        }

        [Test]
        public void MalformedRemote_PlaysTheAsset_AndSaysSo()
        {
            LogAssert.Expect(LogType.Error, new Regex("Error reading remote config ProbeConfig"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[config\] ProbeConfig: remote invalid"));

            _config.InitWithRemote("{not json");

            Assert.That(_config.Current, Is.SameAs(_asset),
                "a JsonReaderException used to escape the catch (it only took JsonSerializationException)");
        }

        [Test]
        public void LiteralNullPayload_PlaysTheAsset()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[config\] ProbeConfig: remote invalid"));

            _config.InitWithRemote("null");

            Assert.That(_config.Current, Is.SameAs(_asset));
        }

        [Test]
        public void ValidRemote_ReplacesTheAsset_NoMerge()
        {
            _config.InitWithRemote("{\"Value\":42}");

            Assert.That(_config.Current, Is.Not.SameAs(_asset));
            Assert.That(_config.Current.Value, Is.EqualTo(42));
            Assert.That(_config.Current.Label, Is.Null,
                "the section is a whole blob: fields the payload omits are NOT taken from the asset");
        }

        private static void SetAssetData(ProbeConfig config, ProbeConfig.Data data)
        {
            var field = typeof(ConfigWithJson<ProbeConfig.Data>)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "ConfigWithJson._data not found");
            field.SetValue(config, data);
        }
    }
}
