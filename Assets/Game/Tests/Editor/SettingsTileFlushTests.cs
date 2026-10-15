using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using Ui.Popups;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-566 QA defect 2: a settings tile's flag must reach the disk at once, not on the next pause/focus/quit — a
    /// kill while the popup has focus lost it. <see cref="SettingsPopup.WriteFlag"/> writes, then flushes. A recording
    /// backend replaces the global <see cref="PlayerPrefsCollection"/> for the test; the previous instance (or null) is
    /// put back by reflection, since the collection has no public reset.
    /// Mutation this catches: dropping the flush (or flushing before the write) — the order assert goes red.
    /// </summary>
    [TestFixture]
    public sealed class SettingsTileFlushTests
    {
        private static readonly FieldInfo InstanceField =
            typeof(PlayerPrefsCollection).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private object _savedInstance;
        private RecordingBackend _backend;

        [SetUp]
        public void SetUp()
        {
            Assume.That(InstanceField, Is.Not.Null, "PlayerPrefsCollection._instance was renamed — update the test");
            _savedInstance = InstanceField.GetValue(null);
            _backend = new RecordingBackend();
            PlayerPrefsCollection.Init(_backend);
        }

        [TearDown]
        public void TearDown() => InstanceField.SetValue(null, _savedInstance);

        [Test]
        public void WriteFlag_WritesThenFlushes()
        {
            var flag = new PlayerPrefsBool("Test.SettingsTile", true);

            SettingsPopup.WriteFlag(flag, false);

            Assert.That(_backend.Log, Is.EqualTo(new[] { "set Test.SettingsTile=0", "flush" }));
            Assert.That(flag.Value, Is.False);
        }

        private sealed class RecordingBackend : IPlayerPrefsBackend
        {
            public readonly List<string> Log = new();
            private readonly Dictionary<string, int> _ints = new();

            public int GetInt(string key, int defaultValue) => _ints.TryGetValue(key, out var v) ? v : defaultValue;
            public float GetFloat(string key, float defaultValue) => defaultValue;
            public string GetString(string key, string defaultValue) => defaultValue;

            public void SetInt(string key, int value)
            {
                _ints[key] = value;
                Log.Add($"set {key}={value}");
            }

            public void SetFloat(string key, float value) => Log.Add($"set {key}={value}");
            public void SetString(string key, string value) => Log.Add($"set {key}={value}");
            public bool HasValue(string key) => _ints.ContainsKey(key);
            public void DeleteValue(string key) => _ints.Remove(key);
            public void DeleteAll() => _ints.Clear();
            public void SaveDataIfChanged() => Log.Add("flush");
        }
    }
}
