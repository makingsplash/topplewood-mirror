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
    // The APK is the PRODUCTION game: it boots through Loading.unity, which raises the gameplay session as a
    // child scope (TASK-138, ADR-0004) — the same path the owner plays in the editor. What stays dev-only is
    // the packaging: Development build, ARM64 only, debug signing, a single .apk. It does NOT touch
    // EditorBuildSettings (scenes are passed as an explicit array), and it does NOT create signing keys:
    // the build is a development build, which Unity signs with the auto-generated debug keystore.
    public static class AndroidDevBuild
    {
        // The production boot (TASK-533). Until then this was ShapeFillBringup.unity with the note "Loading.unity
        // is an unfinished boot" — stale since TASK-138, and the bringup APK lacked the prod graph (WinScreen,
        // hint booster, prod DEV menu), so a device check of it was not a check of the game.
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Loading.unity",
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
