using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    // Dev-only headless entry point for producing a development Android APK from the command line
    // (`-batchmode -executeMethod Game.Editor.AndroidDevBuild.Build`). Exists because the project has
    // no batch build path: JenkinsBuildWindow is an interactive [MenuItem], and nothing calls
    // BuildPipeline.BuildPlayer. Driven by .studio/tools/build-android.sh (TASK-063).
    //
    // Scope is deliberately narrow — this only proves the app compiles under IL2CPP/Android and boots
    // on a device. It does NOT touch EditorBuildSettings (scenes are passed as an explicit array so the
    // unfinished production boot path via Loading.unity is never silently overridden), and it does NOT
    // create signing keys: the build is a development build, which Unity signs with the auto-generated
    // debug keystore.
    public static class AndroidDevBuild
    {
        // Playable bring-up slice. Loading.unity (the production boot) is intentionally excluded — it is
        // an unfinished boot with open TECH_DEBT (see docs/architecture/overview.md), not a target here.
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/ShapeFillBringup.unity",
        };

        private const string OutputDir = "Artifacts/android";
        private const string ApkName = "dropcolorblocks-dev.apk";

        public static void Build()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(outDir);
            string apkPath = Path.Combine(outDir, ApkName);

            // No release keystore in the project (androidcerts/ is gitignored by design). Force the
            // development/debug-signing path so a stale AndroidKeystoreName pointer cannot derail the
            // build. This mirrors the default (useCustomKeystore is already false) and never creates keys.
            PlayerSettings.Android.useCustomKeystore = false;
            // Emit a single .apk, not an .aab — the device install path uses `adb install`.
            EditorUserBuildSettings.buildAppBundle = false;
            // Dev builds ship ARM64 only: the project default (AndroidTargetArchitectures: 3 =
            // ARMv7|ARM64) packs the IL2CPP engine twice and roughly doubles the APK (281 → 143 MB,
            // TASK-119). armeabi-v7a matters only for 32-bit release devices, so dropping it from the
            // release path would be a product decision the owner has not made — it is forced HERE, in
            // the dev-only path, so ProjectSettings.asset (and the release build) keeps both archs.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            };

            Debug.Log($"[AndroidDevBuild] Building development APK → {apkPath} (scenes: {string.Join(", ", Scenes)})");

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidDevBuild] BuildPlayer threw: {e}");
                EditorApplication.Exit(1);
                return;
            }

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[AndroidDevBuild] BUILD FAILED: result={summary.result}, errors={summary.totalErrors}");
                EditorApplication.Exit(1);
                return;
            }

            long size = File.Exists(apkPath) ? new FileInfo(apkPath).Length : 0L;
            // Parseable marker for build-android.sh to lift the path/size out of the Unity log.
            Debug.Log($"[AndroidDevBuild] BUILD OK APK_PATH={apkPath} APK_BYTES={size}");
            EditorApplication.Exit(0);
        }
    }
}
