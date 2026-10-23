using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    // Headless entry points for producing an Android APK/AAB from the command line. Exists because the project
    // has no batch build path: JenkinsBuildWindow is an interactive [MenuItem], and nothing calls
    // BuildPipeline.BuildPlayer. Driven by .studio/tools/build-android.sh (TASK-063, TASK-669, TASK-674).
    //
    // Modes:
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
    //  - release-aab (`PrepareRelease`, then `BuildReleaseBundle`): the release build as an App Bundle for
    //    Google Play, signed with the game's UPLOAD key (Play rejects debug-signed bundles). The key comes only
    //    from the command line — `-studioKeystore <path> -studioKeyalias <alias> -studioKeystorePassFile <path>`
    //    (one line: the store password, which is also the key password) — and a missing or empty piece is a
    //    REFUSED, never a silent fallback to debug signing. The password is never logged, and nothing here calls
    //    SaveAssets after the key is set; the wrapper's ProjectSettings snapshot drops the keystore pointers.
    //
    // The build is the PRODUCTION game: it boots through Loading.unity, which raises the gameplay session as a
    // child scope (TASK-138, ADR-0004) — the same path the owner plays in the editor. What stays dev-only in
    // the APK modes is the packaging: ARM64 only, debug signing, a single .apk. It does NOT touch
    // EditorBuildSettings (scenes are passed as an explicit array), and it does NOT create signing keys. Debug
    // signing comes from useCustomKeystore = false — NOT from the Development flag: without a custom keystore
    // Unity signs ANY build, development or not, with the machine's debug keystore, so a release APK installs
    // over a dev one.
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
        private const string ReleaseAabName = "dropcolorblocks-release.aab";

        private const string KeystoreArg = "-studioKeystore";
        private const string KeyaliasArg = "-studioKeyalias";
        private const string KeystorePassFileArg = "-studioKeystorePassFile";

        // The symbol every cheat path compiles under (Gameplay/Dev/*, DebugData, the DEV button registrations).
        private const string DevSymbol = "GPIE_DEVELOPMENT";

        public static void Build()
        {
            BuildPackage(BuildOptions.Development, ApkName, "debug", null);
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

        // Release, invocation 2 of 2.
        public static void BuildRelease()
        {
            if (!ReleaseDomainIsClean())
                return;

            // Keep the Build Settings window flag from leaking a development build in (as the publisher's
            // builder does).
            EditorUserBuildSettings.development = false;
            BuildPackage(BuildOptions.None, ReleaseApkName, "release", null);
        }

        // Release as a Google Play App Bundle, invocation 2 of 2 (invocation 1 is the same PrepareRelease).
        // The same cheat-free guarantees as BuildRelease, plus upload-key signing from the command line.
        public static void BuildReleaseBundle()
        {
            if (!ReleaseDomainIsClean())
                return;

            if (!TryReadUploadKey(out UploadKey key, out string reason))
            {
                Debug.LogError($"[AndroidDevBuild] RELEASE-AAB REFUSED: {reason}");
                EditorApplication.Exit(1);
                return;
            }

            EditorUserBuildSettings.development = false;
            BuildPackage(BuildOptions.None, ReleaseAabName, "release-aab", key);
        }

        // False (after Exit(1)) when this domain could still contain cheats. The compile-time branch is the
        // proof that PrepareRelease took effect in a previous invocation.
        private static bool ReleaseDomainIsClean()
        {
#if GPIE_DEVELOPMENT
            Debug.LogError($"[AndroidDevBuild] RELEASE REFUSED: editor domain compiled WITH {DevSymbol} — PrepareRelease did not apply (run it as a separate Unity invocation first)");
            EditorApplication.Exit(1);
            return false;
#else
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (SplitDefines(defines).Contains(DevSymbol))
            {
                Debug.LogError($"[AndroidDevBuild] RELEASE REFUSED: {DevSymbol} is back in Android defines (defines='{defines}')");
                EditorApplication.Exit(1);
                return false;
            }
            return true;
#endif
        }

        private sealed class UploadKey
        {
            public string KeystorePath;
            public string Alias;
            // Never logged, never put into an exception message or a marker.
            public string Password;
        }

        // Refusal reasons name the argument or the path, never the password.
        private static bool TryReadUploadKey(out UploadKey key, out string reason)
        {
            key = null;
            string[] args = Environment.GetCommandLineArgs();
            string keystore = ArgValue(args, KeystoreArg);
            string alias = ArgValue(args, KeyaliasArg);
            string passFile = ArgValue(args, KeystorePassFileArg);

            if (string.IsNullOrEmpty(keystore))
            {
                reason = $"{KeystoreArg} <path> not given — refusing to fall back to debug signing";
                return false;
            }
            if (string.IsNullOrEmpty(alias))
            {
                reason = $"{KeyaliasArg} <alias> not given";
                return false;
            }
            if (string.IsNullOrEmpty(passFile))
            {
                reason = $"{KeystorePassFileArg} <path> not given (the password is accepted only from a file)";
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string keystorePath = Path.GetFullPath(Path.Combine(projectRoot, keystore));
            if (!File.Exists(keystorePath))
            {
                reason = $"keystore not found: {keystorePath}";
                return false;
            }

            string passPath = Path.GetFullPath(Path.Combine(projectRoot, passFile));
            string password;
            try
            {
                // One line: the password itself. Only the line break is stripped — spaces may belong to it.
                password = File.ReadLines(passPath).FirstOrDefault()?.TrimEnd('\r');
            }
            catch (Exception e)
            {
                // The type only: the message of an IO exception is not ours to vouch for.
                reason = $"password file not readable: {passPath} ({e.GetType().Name})";
                return false;
            }
            if (string.IsNullOrEmpty(password))
            {
                reason = $"password file is empty: {passPath}";
                return false;
            }

            key = new UploadKey { KeystorePath = keystorePath, Alias = alias, Password = password };
            reason = null;
            return true;
        }

        private static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // uploadKey == null → .apk with debug signing (debug, release); non-null → .aab signed with that key.
        private static void BuildPackage(BuildOptions buildOptions, string fileName, string mode, UploadKey uploadKey)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, fileName);
            bool bundle = uploadKey != null;
            string kind = bundle ? "AAB" : "APK";

            if (bundle)
            {
                // In memory only: no SaveAssets after this point, so this code never writes the keystore
                // pointers to ProjectSettings.asset (Unity does not serialize the passwords at all).
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = uploadKey.KeystorePath;
                PlayerSettings.Android.keystorePass = uploadKey.Password;
                PlayerSettings.Android.keyaliasName = uploadKey.Alias;
                PlayerSettings.Android.keyaliasPass = uploadKey.Password;
            }
            else
            {
                // No release keystore in the APK modes (androidcerts/ is gitignored by design). Force the
                // debug-signing path so a stale AndroidKeystoreName pointer cannot derail the build. This mirrors
                // the default (useCustomKeystore is already false), never creates keys, and is what keeps a
                // release APK installable over a dev one.
                PlayerSettings.Android.useCustomKeystore = false;
            }
            // The APK modes emit a single .apk — the device install path uses `adb install`. The store path
            // needs an .aab.
            EditorUserBuildSettings.buildAppBundle = bundle;
            // Studio builds ship ARM64 only: the project default (AndroidTargetArchitectures: 3 =
            // ARMv7|ARM64) packs the IL2CPP engine twice and roughly doubles the APK (281 → 143 MB,
            // TASK-119). armeabi-v7a matters only for 32-bit store devices, so dropping it from the
            // store path would be a product decision the owner has not made — it is forced HERE, in
            // this script, so ProjectSettings.asset (and the store build) keeps both archs.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildOptions,
            };

            string signing = bundle ? $"upload key alias '{uploadKey.Alias}' from {uploadKey.KeystorePath}" : "debug key";
            Debug.Log($"[AndroidDevBuild] Building {mode} {kind} → {outPath} (options: {buildOptions}, signing: {signing}, scenes: {string.Join(", ", Scenes)})");

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

            long size = File.Exists(outPath) ? new FileInfo(outPath).Length : 0L;
            // Parseable marker for build-android.sh to lift the path/size out of the Unity log. Keep the
            // `BUILD OK` prefix: the tool's regexes depend on it. APK modes print APK_PATH/APK_BYTES, the
            // bundle prints AAB_PATH/AAB_BYTES.
            Debug.Log($"[AndroidDevBuild] BUILD OK MODE={mode} {kind}_PATH={outPath} {kind}_BYTES={size}");
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
