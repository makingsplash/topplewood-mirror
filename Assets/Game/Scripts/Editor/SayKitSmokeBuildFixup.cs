#if UNITY_ANDROID
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEngine;

namespace Game.Editor
{
    // TASK-063 — in-repo, reproducible neutralization of SayKit's Android runtime integration so the
    // app can smoke-boot on a device WITHOUT a registered SayKit app / matching Firebase config.
    //
    // WHY THIS EXISTS (owner decision, ticket comment 2026-08-01 15:44 — "this is a test app, cut/disable
    // everything that blocks the APK from launching", boundaries: no fabricated creds, no bundle rename,
    // changes live IN the repo & reproducible, everything cut recorded in TECH_DEBT with a restore plan):
    //
    //   The project ships the template's original google-services.json (Firebase project
    //   color-tile-sort-stack-puzzle, client com.game.tilesort) — it has NO client for this project's
    //   applicationId com.game.dropcolorblocks. Two consequences, both from the SayKit package:
    //     1. BUILD: SayKit unconditionally injects `apply plugin: 'com.google.gms.google-services'` +
    //        crashlytics into launcher/build.gradle. The google-services plugin aborts gradle packaging
    //        with "No matching client found for package name 'com.game.dropcolorblocks'".
    //     2. RUNTIME: the SayKit aar declares a ContentProvider (saygames.saykit.SayKitContentProvider)
    //        that runs at PROCESS CREATION (before Application.onCreate / any Unity scene) and calls
    //        FirebaseCrashlytics.getInstance(), which throws "Default FirebaseApp is not initialized" and
    //        kills the process before the Unity runtime ever loads (proven by logcat in run 02).
    //     3. RUNTIME: SayKit also makes its own activity (SayKitBridgeActivity, set by SKManifestInjector as
    //        by.saygames.SayKitActivity) the LAUNCHER. Its onCreate → SayKit.attachActivity →
    //        FirebaseCrashlytics.getInstance() → the SAME "Default FirebaseApp not initialized" crash, one
    //        step later than (2). Removing only the provider just moves the crash here (proven by logcat in
    //        run 03), so the launcher activity is reverted to Unity's own entry point.
    //
    // All three blockers live in SayKit's build-time injectors (Library/PackageCache, gitignored), which run
    // in IPostGenerateGradleAndroidProject at callbackOrder int.MaxValue. This hook runs AFTER them — enabled
    // by the editor-only define SAYKIT_SHIFT_ANDROID_POSTPROCESS_ORDER (added to ProjectSettings Android
    // defines), which shifts SayKit's post-processor down to int.MaxValue-1 so this one (int.MaxValue) is
    // last — and undoes exactly those blockers, editing the FINAL exported gradle project:
    //     A. strips the two Firebase apply-plugin lines from launcher/build.gradle (fixes the build);
    //     B. injects `<provider ... tools:node="remove"/>` so the Android manifest merger drops the
    //        crashing ContentProvider (fixes runtime crash 2);
    //     C. renames the LAUNCHER activity from by.saygames.SayKitActivity back to Unity's own
    //        com.unity3d.player.UnityPlayerActivity (androidApplicationEntry=1/Activity), so SayKit's
    //        Firebase-touching activity onCreate never runs (fixes runtime crash 3).
    //
    // No credentials are fabricated: Firebase Analytics/Crashlytics are simply absent from this smoke APK.
    //
    // REVERSIBLE — restore plan (when the game gets its own Firebase app + SayKit app):
    //   1. add a valid google-services.json for com.game.dropcolorblocks under Assets/Plugins/Android/;
    //   2. delete THIS file (SayKitSmokeBuildFixup.cs + .meta);
    //   3. remove SAYKIT_SHIFT_ANDROID_POSTPROCESS_ORDER from ProjectSettings Android scriptingDefineSymbols;
    //   4. drop the matching TECH_DEBT.md entry.
    //
    // NOTE: this does NOT resolve the SayKit EDITOR pre-build gate (SayKitPreBuild throws "Can't find app"
    // on the empty app_key) — that gate cannot be disabled from repo files (see the artifact / TECH_DEBT).
    public class SayKitSmokeBuildFixup : IPostGenerateGradleAndroidProject
    {
        // Must be last. SAYKIT_SHIFT_ANDROID_POSTPROCESS_ORDER moves SayKit's own post-processor to
        // int.MaxValue-1, so int.MaxValue here runs strictly after SayKit's manifest/gradle injection.
        public int callbackOrder => int.MaxValue;

        private const string Tag = "[SayKitSmokeBuildFixup]";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";
        private const string CrashingProvider = "saygames.saykit.SayKitContentProvider";
        // SKManifestInjector renames Unity's launcher activity to this; its onCreate touches Firebase.
        private const string SayKitLauncherActivity = "by.saygames.SayKitActivity";
        // Unity's own entry point for androidApplicationEntry=1 (Activity). Reverting to it drops SayKit's
        // activity bridge, so no Firebase-touching onCreate runs — the app boots straight into Unity.
        private const string UnityLauncherActivity = "com.unity3d.player.UnityPlayerActivity";

        // Lines SayKit appends to launcher/build.gradle for the Google Play store target. Removing the
        // `apply plugin` for google-services is what avoids the "No matching client found" gradle abort.
        private static readonly string[] FirebasePluginNeedles =
        {
            "com.google.gms.google-services",
            "com.google.firebase.crashlytics",
        };

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string launcherPath = unityLibraryPath.Replace("unityLibrary", "launcher");
            string launcherManifest = Path.Combine(launcherPath, "src", "main", "AndroidManifest.xml");
            // Unity declares the game activity in the unityLibrary manifest (the launcher module is a thin
            // wrapper). SayKit renames it there, so the activity revert must target the unityLibrary manifest.
            string unityLibraryManifest = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");

            StripFirebaseLauncherPlugins(Path.Combine(launcherPath, "build.gradle"));
            RemoveCrashingProvider(launcherManifest);
            RevertLauncherActivity(unityLibraryManifest);
        }

        // (A) Remove `apply plugin: '...google-services'` / `...crashlytics` from launcher/build.gradle.
        private static void StripFirebaseLauncherPlugins(string gradlePath)
        {
            if (!File.Exists(gradlePath))
            {
                Debug.LogWarning($"{Tag} launcher build.gradle not found at {gradlePath}; nothing to strip.");
                return;
            }

            string[] lines = File.ReadAllLines(gradlePath);
            var kept = lines.Where(line =>
            {
                bool isApplyPlugin = line.Contains("apply plugin");
                bool hitsFirebase = FirebasePluginNeedles.Any(line.Contains);
                return !(isApplyPlugin && hitsFirebase);
            }).ToArray();

            if (kept.Length != lines.Length)
            {
                File.WriteAllLines(gradlePath, kept);
                Debug.Log($"{Tag} stripped {lines.Length - kept.Length} Firebase apply-plugin line(s) from {gradlePath}.");
            }
            else
            {
                Debug.LogWarning($"{Tag} no Firebase apply-plugin lines found in {gradlePath} (SayKit injector ran? define set?).");
            }
        }

        // (B) Inject a provider node with tools:node="remove" into the launcher (app) manifest so the
        // manifest merger drops the SayKit ContentProvider (contributed by the SayKit aar) that crashes the
        // process on Firebase init. Proven effective in run 03.
        private static void RemoveCrashingProvider(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"{Tag} launcher manifest not found at {manifestPath}; cannot remove provider.");
                return;
            }

            XNamespace android = AndroidNs;
            XNamespace tools = ToolsNs;

            XDocument doc = XDocument.Load(manifestPath);
            XElement manifest = doc.Root;
            if (manifest == null)
            {
                Debug.LogError($"{Tag} {manifestPath} has no root <manifest>; cannot remove provider.");
                return;
            }

            if (manifest.Attribute(XNamespace.Xmlns + "tools") == null)
            {
                manifest.SetAttributeValue(XNamespace.Xmlns + "tools", ToolsNs);
            }

            XElement application = manifest.Element("application");
            if (application == null)
            {
                application = new XElement("application");
                manifest.Add(application);
            }

            bool alreadyPresent = application.Elements("provider").Any(p =>
                (string)p.Attribute(android + "name") == CrashingProvider);
            if (alreadyPresent)
            {
                Debug.Log($"{Tag} remove-directive for {CrashingProvider} already present.");
                return;
            }

            var provider = new XElement("provider");
            provider.SetAttributeValue(android + "name", CrashingProvider);
            provider.SetAttributeValue(tools + "node", "remove");
            application.Add(provider);

            doc.Save(manifestPath);
            Debug.Log($"{Tag} injected tools:node=\"remove\" for {CrashingProvider} into {manifestPath}.");
        }

        // (C) Rename the game activity — declared in the unityLibrary manifest and renamed to
        // by.saygames.SayKitActivity by SKManifestInjector — back to Unity's own entry point, so SayKit's
        // Firebase-touching activity onCreate never runs. Matches by the SayKit name, falling back to the
        // activity carrying the MAIN/LAUNCHER intent-filter so it survives a future SayKit rename.
        private static void RevertLauncherActivity(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"{Tag} unityLibrary manifest not found at {manifestPath}; cannot revert activity.");
                return;
            }

            XNamespace android = AndroidNs;

            XDocument doc = XDocument.Load(manifestPath);
            XElement application = doc.Root?.Element("application");
            if (application == null)
            {
                Debug.LogError($"{Tag} {manifestPath} has no <application>; cannot revert activity.");
                return;
            }

            XElement launcher =
                application.Elements("activity").FirstOrDefault(a =>
                    (string)a.Attribute(android + "name") == SayKitLauncherActivity)
                ?? application.Elements("activity").FirstOrDefault(HasLauncherIntent);

            if (launcher == null)
            {
                Debug.LogWarning($"{Tag} no launcher activity found in {manifestPath}; SayKit activity may still be entry point.");
                return;
            }

            string current = (string)launcher.Attribute(android + "name");
            if (current == UnityLauncherActivity)
            {
                Debug.Log($"{Tag} launcher activity already {UnityLauncherActivity}.");
                return;
            }

            launcher.SetAttributeValue(android + "name", UnityLauncherActivity);
            doc.Save(manifestPath);
            Debug.Log($"{Tag} reverted launcher activity {current} → {UnityLauncherActivity} in {manifestPath}.");
        }

        private static bool HasLauncherIntent(XElement activity)
        {
            XNamespace android = AndroidNs;
            return activity.Elements("intent-filter").Any(f =>
                f.Elements("action").Any(a => (string)a.Attribute(android + "name") == "android.intent.action.MAIN") &&
                f.Elements("category").Any(c => (string)c.Attribute(android + "name") == "android.intent.category.LAUNCHER"));
        }
    }
}
#endif
