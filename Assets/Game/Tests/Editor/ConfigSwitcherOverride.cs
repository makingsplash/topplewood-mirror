using System;
using System.Reflection;
using NUnit.Framework;
using SGS.Infrastructure.Tools.Configs;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-064: forces <see cref="ConfigSwitcher.UseLocalConfig"/> to <c>false</c> for the lifetime of the scope, so a
    /// config test drives the REMOTE branch of <c>ConfigWithJson.InitWithRemote</c>. Without it the editor default
    /// (local mode, <c>UserSettings/ConfigSwitcher.json</c> absent → <c>true</c>) serves the asset whatever the payload
    /// says, and a test of the remote path would be green for nothing (docs/platform/testing.md). The cached value is
    /// restored on dispose — including "not read yet" (null), so the next reader loads the user's setting again.
    /// </summary>
    internal sealed class ConfigSwitcherOverride : IDisposable
    {
        private static readonly FieldInfo CachedField =
            typeof(ConfigSwitcher).GetField("_useLocalConfig", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly object _saved;

        public ConfigSwitcherOverride()
        {
            Assert.That(CachedField, Is.Not.Null,
                "ConfigSwitcher._useLocalConfig not found — the package changed; fix the override, not the tests.");
            _saved = CachedField.GetValue(null);
            CachedField.SetValue(null, (bool?)false);
            Assert.That(ConfigSwitcher.UseLocalConfig, Is.False, "the override did not reach ConfigSwitcher");
        }

        public void Dispose()
        {
            CachedField.SetValue(null, _saved);
        }
    }
}
