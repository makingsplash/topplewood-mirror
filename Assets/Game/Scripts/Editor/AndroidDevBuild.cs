using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    // Headless entry points for producing an Android APK from the command line. Exists because the project
    // has no batch build path: JenkinsBuildWindow is an interactive [MenuItem], and nothing calls
    // BuildPipeline.BuildPlayer. Driven by .studio/tools/build-android.sh (TASK-063, TASK-669).
    //
    // Two modes:
    //  - debug   (`-executeMethod Game.Editor.AndroidDevBuild.Build`): BuildOptions.Development, the project
    //    defines as they are (GPIE_DEVELOPMENT on → DEV button and cheat menu in the APK).
    //  - release (TWO Unity invocations, in order): `PrepareRelease` strips GPIE_DEVELOPMENT from the Android
    //    defines and saves ProjectSettings; then `BuildRelease` builds with BuildOptions.None in a domain that
    //    was compiled WITHOUT the symbol. One invocation is not enough: defines set from script apply only
    //    after the editor recompiles, and batchmode does not recompile before BuildPlayer, so editor code
    //    running during the build would keep the old symbols (Unity Manual, "Custom scripting symbols"). The
    //    publisher's own builder splits it the same way (Packages/com.say.build Builder.cs, "Prepare Android
    //    RELEASE" / "Build Android RELEASE"). Restoring ProjectSettings is the wrapper's job (byte snapshot),
    //    not this file's: a C# finally would not run on an Exit(1) from inside the SayKit prebuild.
    //
    // The APK is the PRODUCTION game: it boots through Loading.unity, which raises the gameplay session as a
    // child scope (TASK-138, ADR-0004) — the same path the owner plays in the editor. What stays dev-only is
    // the packaging: ARM64 only, debug signing, a single .apk. It does NOT touch EditorBuildSettings (scenes
    // are passed as an explicit array), and it does NOT create signing keys. Debug signing comes from
    // useCustomKeystore = false — NOT from the Development flag: without a custom keystore Unity signs ANY
    // build, development or not, with the machine's debug keystore, so a release APK installs over a dev one.
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
        private const string ReleaseApkName = "dropcolorblocks-release.apk";

        // The symbol every cheat path compiles under (Gameplay/Dev/*, DebugData, the DEV button registrations).
        private const string DevSymbol = "GPIE_DEVELOPMENT";

        public static void Build()
        {
            BuildApk(BuildOptions.Development, ApkName, "debug");
        }

        // Release, invocation 1 of 2. Refuses when there is nothing to strip: a "release" that removed no
        // symbol would be a dev APK under a release name (the template's release mode does exactly that when
        // its default debug symbol is not the project's — TASK-669 §1).
        public static void PrepareRelease()
        {
            NamedBuildTarget named = NamedBuildTarget.Android;
            string before = PlayerSettings.GetScriptingDefineSymbols(named);
            string[] symbols = SplitDefines(before);

            if (!symbols.Contains(DevSymbol))
            {
                Debug.LogError($"[AndroidDevBuild] PREPARE REFUSED: {DevSymbol} not in Android defines — nothing to strip; refusing to call this a release (defines='{before}')");
                EditorApplication.Exit(1);
                return;
            }

            string after = string.Join(";", symbols.Where(s => s != DevSymbol));
            PlayerSettings.SetScriptingDefineSymbols(named, after);
            // ProjectSettings must be on disk before this invocation exits: invocation 2 reads it at startup.
            AssetDatabase.SaveAssets();

            Debug.Log($"[AndroidDevBuild] PREPARE OK defines_before={before} defines_at_build={after}");
            EditorApplication.Exit(0);
        }

        // Release, invocation 2 of 2. Must start in a domain compiled without the symbol — the compile-time
        // check below is the proof that invocation 1 took effect.
        public static void BuildRelease()
        {
#if GPIE_DEVELOPMENT
            Debug.LogError($"[AndroidDevBuild] RELEASE REFUSED: editor domain compiled WITH {DevSymbol} — PrepareRelease did not apply (run it as a separate Unity invocation first)");
            EditorApplication.Exit(1);
            return;
#else
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (SplitDefines(defines).Contains(DevSymbol))
            {
                Debug.LogError($"[AndroidDevBuild] RELEASE REFUSED: {DevSymbol} is back in Android defines (defines='{defines}')");
                EditorApplication.Exit(1);
                return;
            }

            // Keep the Build Settings window flag from leaking a development build in (as the publisher's
            // builder does).
            EditorUserBuildSettings.development = false;
            BuildApk(BuildOptions.None, ReleaseApkName, "release");
#endif
        }

        private static void BuildApk(BuildOptions buildOptions, string apkName, string mode)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(outDir);
            string apkPath = Path.Combine(outDir, apkName);

            // No release keystore in the project (androidcerts/ is gitignored by design). Force the
            // debug-signing path so a stale AndroidKeystoreName pointer cannot derail the build. This mirrors
            // the default (useCustomKeystore is already false), never creates keys, and is what keeps a
            // release APK installable over a dev one.
            PlayerSettings.Android.useCustomKeystore = false;
            // Emit a single .apk, not an .aab — the device install path uses `adb install`.
            EditorUserBuildSettings.buildAppBundle = false;
            // Studio builds ship ARM64 only: the project default (AndroidTargetArchitectures: 3 =
            // ARMv7|ARM64) packs the IL2CPP engine twice and roughly doubles the APK (281 → 143 MB,
            // TASK-119). armeabi-v7a matters only for 32-bit store devices, so dropping it from the
            // store path would be a product decision the owner has not made — it is forced HERE, in
            // this script, so ProjectSettings.asset (and the store build) keeps both archs.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildOptions,
            };

            Debug.Log($"[AndroidDevBuild] Building {mode} APK → {apkPath} (options: {buildOptions}, scenes: {string.Join(", ", Scenes)})");

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
                Debug.LogError($"[AndroidDevBuild] BUILD FAILED: mode={mode}, result={summary.result}, errors={summary.totalErrors}");
                EditorApplication.Exit(1);
                return;
            }

            long size = File.Exists(apkPath) ? new FileInfo(apkPath).Length : 0L;
            // Parseable marker for build-android.sh to lift the path/size out of the Unity log. Keep the
            // `BUILD OK` prefix: the tool's regexes depend on it.
            Debug.Log($"[AndroidDevBuild] BUILD OK MODE={mode} APK_PATH={apkPath} APK_BYTES={size}");
            EditorApplication.Exit(0);
        }

        private static string[] SplitDefines(string defines)
        {
            return defines
                .Split(';')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }
    }
}
