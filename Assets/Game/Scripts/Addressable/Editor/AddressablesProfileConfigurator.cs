#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Editor
{
    public static class AddressablesProfileConfigurator
    {
        private const string ReleaseProfileName       = "Release";
        private const string DevelopProfileName       = "Develop";
        private const string DevelopRemoteProfileName = "Develop_remote";

        [MenuItem("Builds/Prepare/SetupProfile")]
        public static void SetupProfile()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found.");
                return;
            }

            string configuration = Environment.GetEnvironmentVariable("configuration");
            configuration = string.IsNullOrEmpty(configuration)
                ? "dev"
                : configuration.ToLowerInvariant();

            string remoteTestFlag = Environment.GetEnvironmentVariable("dev_remote");
            bool useRemoteTest = string.Equals(remoteTestFlag, "true", StringComparison.OrdinalIgnoreCase);

            string targetProfileName;
            if (configuration == "release")
                targetProfileName = ReleaseProfileName;
            else
                targetProfileName = useRemoteTest ? DevelopRemoteProfileName : DevelopProfileName;

            string targetProfileId = settings.profileSettings.GetProfileId(targetProfileName);
            if (string.IsNullOrEmpty(targetProfileId))
            {
                Debug.LogError($"Profile '{targetProfileName}' not found.");
                return;
            }

            settings.activeProfileId = targetProfileId;
            Debug.Log($"[AddressablesProfileConfigurator] Using profile '{targetProfileName}' ({targetProfileId}), configuration='{configuration}', remote_test={useRemoteTest}.");

            string bundleVersion = Environment.GetEnvironmentVariable("BUILD_NUMBER");
            if (string.IsNullOrEmpty(bundleVersion))
                bundleVersion = "local";

            settings.profileSettings.SetValue(targetProfileId, "BundleVersion", bundleVersion);
            Debug.Log($"[AddressablesProfileConfigurator] BundleVersion set to '{bundleVersion}'.");

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
