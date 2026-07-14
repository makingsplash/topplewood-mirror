using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if HAVE_PURCHASING
using UnityEngine.Purchasing;
using UnityEditor.Purchasing;
#endif

namespace Editor
{
    public class Builder
    {
        private static bool _isAndroidAab = true;

        private static readonly string[] _developmentDefineSymbols = {
            "GPIE_DEVELOPMENT",
            //"SAYKIT_UPLOAD_SYMB_DISABLE"
        };

        private static readonly string[] _autotestDefineSymbols = {
            "AUTOTESTS"
        };

        private static readonly string[] _googleStoreDefineSymbols = {
            "GPIE_GOOGLE_STORE"
        };
        private static readonly string[] _amazonStoreDefineSymbols = {
            "GPIE_AMAZON_STORE"
        };

        private static readonly string[] _purchaseDefineSymbols = {
            "SAYKIT_PURCHASING"
        };

        private const string _storeSettingsXml = "androidcerts/store_settings.xml";
        private const string _defaultKeystoreXml = "androidcerts/keystore_info.xml";

        
        [MenuItem("Builds/Prepare/Prepare Autotests Android")]
        private static void Prepare_autotests_android()
        {
            SetDefines(BuildTargetGroup.Android, _developmentDefineSymbols, true);
            SetDefines(BuildTargetGroup.Android, _autotestDefineSymbols, true);
        }
    
        [MenuItem("Builds/Prepare/Prepare Autotests iOS")]
        private static void Prepare_autotests_ios()
        {
            SetDefines(BuildTargetGroup.iOS, _developmentDefineSymbols, true);
            SetDefines(BuildTargetGroup.iOS, _autotestDefineSymbols, true);
        }

        [MenuItem("Builds/Prepare/Set Android apk")]
        private static void Prepare_android_apk()
        {
            _isAndroidAab = false;
        }
    
        [MenuItem("Builds/Prepare/Set Android aab")]
        private static void Prepare_android_aab()
        {
            _isAndroidAab = true;
        }

        [MenuItem("Builds/Prepare/Prepare Android DEV")]
        private static void Prepare_android_dev()
        {
            SetDefines(BuildTargetGroup.Android, _developmentDefineSymbols, true);
            SetDefines(BuildTargetGroup.Android, _autotestDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _googleStoreDefineSymbols, true);
            SetDefines(BuildTargetGroup.Android, _amazonStoreDefineSymbols, false);

            PrepareAndroidGoogleCommon();
        }

        [MenuItem("Builds/Prepare/Prepare Amazon RELEASE")]
        private static void Prepare_amazon_release()
        {
            SetDefines(BuildTargetGroup.Android, _developmentDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _autotestDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _googleStoreDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _amazonStoreDefineSymbols, true);

            PrepareAndroidAmazonCommon();
        }
        [MenuItem("Builds/Prepare/Prepare Amazon DEV")]
        private static void Prepare_amazon_dev()
        {
            SetDefines(BuildTargetGroup.Android, _developmentDefineSymbols, true);
            SetDefines(BuildTargetGroup.Android, _autotestDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _googleStoreDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _amazonStoreDefineSymbols, true);

            PrepareAndroidAmazonCommon();
        }

        [MenuItem("Builds/Prepare/Prepare Android RELEASE")]
        private static void Prepare_android_release()
        {
            SetDefines(BuildTargetGroup.Android, _developmentDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _autotestDefineSymbols, false);
            SetDefines(BuildTargetGroup.Android, _googleStoreDefineSymbols, true);
            SetDefines(BuildTargetGroup.Android, _amazonStoreDefineSymbols, false);

            PrepareAndroidGoogleCommon();
        }
        
        [MenuItem("Builds/Prepare/Prepare iOS DEV")]
        private static void Prepare_ios_dev()
        {
            SetDefines(BuildTargetGroup.iOS, _developmentDefineSymbols, true);
            SetDefines(BuildTargetGroup.iOS, _autotestDefineSymbols, false);

            PrepareIosCommon();
        }

        [MenuItem("Builds/Prepare/Prepare iOS RELEASE")]
        private static void Prepare_ios_release()
        {
            SetDefines(BuildTargetGroup.iOS, _developmentDefineSymbols, false);
            SetDefines(BuildTargetGroup.iOS, _autotestDefineSymbols, false);
            PrepareIosCommon();
        }
    
        [MenuItem("Builds/Build Android DEV")]
        private static void PerformBuild_android_dev()
        {
            ApplyAndroidConfig("google");
#if HAVE_PURCHASING
            UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif
            PerformBuildAndroid();
        }

        [MenuItem("Builds/Build Android RELEASE")]
        private static void PerformBuild_android_release()
        {
            ApplyAndroidConfig("google");
#if HAVE_PURCHASING
            UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif
            PerformBuildAndroid();
        }
        
        [MenuItem("Builds/Build Amazon DEV")]
        private static void PerformBuild_amazon_dev()
        {
            ApplyAndroidConfig("amazon");
            // IAP 5.x: поддержка Amazon Appstore удалена из com.unity.purchasing.
            // Таргетинг IAP на Amazon недоступен; сборка для Amazon идёт без IAP-таргетинга стора.
            PerformBuildAndroid();
        }

        [MenuItem("Builds/Build Amazon RELEASE")]
        private static void PerformBuild_amazon_release()
        {
            ApplyAndroidConfig("amazon");
            // IAP 5.x: поддержка Amazon Appstore удалена из com.unity.purchasing.
            // Таргетинг IAP на Amazon недоступен; сборка для Amazon идёт без IAP-таргетинга стора.
            PerformBuildAndroid();
        }
        
        
        [MenuItem("Builds/Build iOS DEV")]
        private static void PerformBuild_ios_dev()
        {
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
            PerformBuildIos();
        }

        [MenuItem("Builds/Build iOS RELEASE")]
        private static void PerformBuild_ios_release()
        {
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
            PerformBuildIos();
        }

        private static void PerformBuildAndroid()
        {
            Debug.Log("==================== Unity environment ==========================");
            foreach (DictionaryEntry dictionaryEntry in Environment.GetEnvironmentVariables())
            {
                Debug.Log($"Key: {dictionaryEntry.Key} Entry: {dictionaryEntry.Value}");
            }
            Debug.Log("================ End Unity environment ==========================");
        
            AndroidExternalToolsSettings.Gradle.path = null;
            AndroidExternalToolsSettings.Gradle.stopDaemonsOnExit = false;
            string pathToGradle = GetPathToGradle();

            if (string.IsNullOrEmpty(pathToGradle))
            {
                throw new ArgumentException("path to gradle was not found!");
            }
        
            AndroidExternalToolsSettings.Gradle.path = pathToGradle;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ETC2;
            EditorUserBuildSettings.buildAppBundle = _isAndroidAab;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;

            EditorUserBuildSettings.development = false;


            string[] scenes = GetScenes();
            string buildPath = "build/Android";
            Directory.CreateDirectory(buildPath);
            buildPath += _isAndroidAab? "/build.aab" : "/build.apk";

            BuildReport report = BuildPipeline.BuildPlayer(scenes, buildPath, BuildTarget.Android, BuildOptions.None);
            if (report.summary.result == BuildResult.Failed)
            {
                EditorApplication.Exit(1);
            }
        }

        private static void ApplyAndroidConfig(string configId)
        {
            if (!File.Exists(_storeSettingsXml))
            {
                Debug.Log($"File {_storeSettingsXml} not found, using default keystore_info file");
                ReadKeystore( _defaultKeystoreXml);
                return;
            }
            
            XmlDocument xmlDocument = new XmlDocument();
            string text = File.ReadAllText(_storeSettingsXml);
            Debug.Log(text);
            xmlDocument.LoadXml(text);
            Debug.Log(xmlDocument.DocumentElement?.InnerXml);

            var version = xmlDocument.GetElementById(configId);
            if (version == null)
            {
                Debug.LogError($"Version {configId} not found in shop_versions.xml"); 

                return;
            }

            string bundleID = null;
            string keystoreFile = null;
            foreach (var props in version.GetElementsByTagName("prop").OfType<XmlElement>())
            {
                if (props.GetAttribute("id").Equals("bundle_id"))
                {
                    bundleID = props.GetAttribute("value");
                }
                else if (props.GetAttribute("id").Equals("keystore_path"))
                {
                    keystoreFile = props.GetAttribute("value");
                }
            }
                
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, bundleID);
            ReadKeystore(keystoreFile);
        }
        
        private static void ReadKeystore(string infoFile)
        {
            XmlDocument xmlDocument = new XmlDocument();
            string text = File.ReadAllText(infoFile);
            Debug.Log(text);
            xmlDocument.LoadXml(text);
            Debug.Log(xmlDocument.DocumentElement?.InnerXml);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = xmlDocument.GetElementById("keystore_path").GetAttribute("value");
            PlayerSettings.Android.keystorePass = xmlDocument.GetElementById("keystore_password").GetAttribute("value");
            PlayerSettings.Android.keyaliasName = xmlDocument.GetElementById("key_name").GetAttribute("value");
            PlayerSettings.Android.keyaliasPass = xmlDocument.GetElementById("key_password").GetAttribute("value");
        }

        private static void PrepareAndroidGoogleCommon()
        {
            bool china = "${china_version}" == "true";
            Debug.Log("China version: " + china);

            string buildNumber = GetBuildNumber();
            TryUpdateBundleVersionCode(buildNumber);
            SetBillingMode("GooglePlay");
#if HAVE_PURCHASING
            UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif
// #if UNITY_ANDROID
//                 Utils.Editor.DevTools.IconReplaceTool.ReplaceAndroidIcons(china);
// #endif
        
#if BOT_ENABLED
            bool bot = "${bot_version}" == "true";
            Debug.Log("Bot version: " + bot);
            
            Utils.Editor.DevTools.SceneChangeTool.SetDefaultSceneList(bot);
#endif
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        private static void PrepareAndroidAmazonCommon()
        {
            bool china = "${china_version}" == "true";
            Debug.Log("China version: " + china);

            string buildNumber = GetBuildNumber();
            TryUpdateBundleVersionCode(buildNumber);
            SetBillingMode("AmazonAppStore");

            // #if UNITY_ANDROID
            //                 Utils.Editor.DevTools.IconReplaceTool.ReplaceAndroidIcons(china);
            // #endif

#if BOT_ENABLED
            bool bot = "${bot_version}" == "true";
            Debug.Log("Bot version: " + bot);
            
            Utils.Editor.DevTools.SceneChangeTool.SetDefaultSceneList(bot);
#endif
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        private static string GetPathToGradle()
        {
            string pathToGradle = Environment.GetEnvironmentVariable("PATH_TO_GRADLE") ?? "";
            return pathToGradle.Trim(' ', '\t');
        }

        private static void TryUpdateBundleVersionCode(string buildNumber)
        {
            if (!string.IsNullOrEmpty(buildNumber))
            {
                bool success = int.TryParse(buildNumber, out int bundleVersionCode);
                if (success)
                {
                    PlayerSettings.Android.bundleVersionCode = bundleVersionCode;
                    PlayerSettings.iOS.buildNumber = bundleVersionCode.ToString();
                }
                else
                {
                    Debug.LogError("Can't parse build number");
                }
            }
        }

        private static void PrepareIosCommon()
        {
            bool china = "${china_version}" == "true";
            Debug.Log("China version: " + china);

            string buildNumber = GetBuildNumber();
            TryUpdateBundleVersionCode(buildNumber);

// #if UNITY_IOS
//                 Utils.Editor.DevTools.IconReplaceTool.ReplaceIosIcons(china);
// #endif

#if BOT_ENABLED
        bool bot = "${bot_version}" == "true";
        Debug.Log("Bot version: " + bot);
        
        Utils.Editor.DevTools.SceneChangeTool.SetDefaultSceneList(bot);
#endif

#if !SUPRESS_SAYKIT_BUILD
            if (china)
            {
                //SayKitInternal.Editor.ChinaConfig();
            }
            else
            {
                //SayKitInternal.Editor.WorldConfig();
            }
#endif
            
#if DISABLE_PURCHASES
            SetDefines(BuildTargetGroup.iOS, _purchaseDefineSymbols, false);
#endif
        }

        private static void PerformBuildIos()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.iOS;

            EditorUserBuildSettings.development = false;

            string[] scenes = GetScenes();
            string buildPath = "build/iOS";
            Directory.CreateDirectory(buildPath);
        
            BuildReport report = BuildPipeline.BuildPlayer(scenes, buildPath, BuildTarget.iOS, BuildOptions.None);
            if (report.summary.result == BuildResult.Failed)
            {
                EditorApplication.Exit(1);
            }
        }


        // Добавляем выбранные в настройках сцены в билд
        private static string[] GetScenes()
        {
            var projectScenes = EditorBuildSettings.scenes;
            List<string> scenesToBuild = new List<string>();
            for (int i = 0; i < projectScenes.Length; i++)
            {
                if (projectScenes[i].enabled)
                {
                    scenesToBuild.Add(projectScenes[i].path);
                }
            }
            return scenesToBuild.ToArray();
        }

        private static void SetDefines(BuildTargetGroup btg, string[] defines, bool enable)
        {
            string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(btg);
            List<string> allDefines = definesString.Split(';').ToList();
            if (enable)
            {
                allDefines.AddRange(defines.Except(allDefines));
            }
            else
            {
                allDefines = allDefines.Except(defines).ToList();
            }

            PlayerSettings.SetScriptingDefineSymbolsForGroup(btg, string.Join(";", allDefines.ToArray()));
        }

        [MenuItem("Builds/Addressables/Build Addressables")]
        private static void BuildAddressables()
        {
#if ADDRESSABLES_ENABLED
        string buildScriptPath = "Assets/AddressableAssetsData/DataBuilders/BuildScriptPackedMode.asset"; 
        string settingsAsset = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
        string profileName = "Default";
        AddressableAssetSettings settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingsAsset) as AddressableAssetSettings;

        if (settings == null)
        {
            Debug.LogError(settingsAsset + " couldn't be found or isn't a settings object.");
            EditorApplication.Exit(1);
        }
        
        string profileId = settings.profileSettings.GetProfileId(profileName);
        if (String.IsNullOrEmpty(profileId))
        {
            Debug.LogWarning("Couldn't find a profile named, " + profileName +", using current profile instead.");
        }
        else
        {
            settings.activeProfileId = profileId;
        }

        IDataBuilder builderScript = AssetDatabase.LoadAssetAtPath<ScriptableObject>(buildScriptPath) as IDataBuilder;
        if (builderScript == null) 
        {
            Debug.LogError(buildScriptPath + " couldn't be found or isn't a build script.");
            EditorApplication.Exit(1);
        }
        int index = settings.DataBuilders.IndexOf((ScriptableObject)builderScript);
        if (index > 0)
        {
            settings.ActivePlayerDataBuilderIndex = index;
        }
        else
        {
            Debug.LogWarning(builderScript + " must be added to the DataBuilders list before it can be made active. Using last run builder instead.");
        }
        
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        bool success = string.IsNullOrEmpty(result.Error);

        if (!success) {
            Debug.LogError("Addressables build error encountered: " + result.Error);
            EditorApplication.Exit(1);
        }

        Debug.Log("Building addressables succeeded");
#endif
        }

        private static string GetBuildNumber()
        {
            string buildNumber = null;
            TextAsset buildnumberText = Resources.Load("build_number") as TextAsset;
            if (buildnumberText != null)
            {
                buildNumber = buildnumberText.text.Replace(System.Environment.NewLine, "");
            }

            return buildNumber;
        }
        
        private static void SetBillingMode(string billingMode)
        {
            var path = Path.Combine(Application.dataPath, "Resources/BillingMode.json");
            var json = $"{{\"androidStore\":\"{billingMode}\"}}";
            File.WriteAllText(path, json);
        }
    }
}