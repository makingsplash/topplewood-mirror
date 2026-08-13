using DataStorage;
using SayKitInternal;
using UnityEngine;

namespace Infrastructure.Loading
{
    public static class SayKitInit
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
#if SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR
            // TASK-063 (owner-approved local smoke build): this project has no registered SayKit app and
            // ships no matching Firebase config, and SayKit's Android entry points (ContentProvider +
            // bridge activity) are stripped from the smoke APK's manifest — see
            // Editor/SayKitSmokeBuildFixup.cs and TECH_DEBT TD-001. With the SayKit Java bridge therefore
            // never initialized, this BeforeSceneLoad call to SayKit.init() throws
            // NoClassDefFoundError(saygames.bridge.unity.SayKitBridge) / UninitializedPropertyAccessException
            // every frame, which prevents the scene from rendering (black screen, ~74 err/s measured).
            // Neutralize the runtime entry so the smoke build can open the first level.
            // The SAYKIT_SMOKE_DISABLE_RUNTIME define lives in the Android scripting-defines list. The
            // editor's active build target IS Android, so this define IS active in the editor too (TASK-129
            // corrected the earlier claim that editor behaviour was untouched — it was not: play-mode boot
            // hung forever because SayKit.init() never ran). The `&& !UNITY_EDITOR` guard restores that:
            // the crash only exists in the Java player, so in-editor SayKit still initializes through
            // SayKitBridgeEditor (which never touches the Java bridge) and the prod boot reaches Done.
            // Reversible: drop the define once the game has its own SayKit/Firebase app (TD-001).
#else
            SayKitConfig config = new SayKitConfig
            {
                attributionConfigUpdate = true
            };

            config.remoteConfigUpdated += RemoteConfigUpdated;
#if UNITY_IOS
            config.appKey = SayKitApp.APP_KEY_IOS;
#elif UNITY_ANDROID
            config.appKey = SayKitApp.APP_KEY_ANDROID;
#endif
            SayKit.init(config);
#endif
        }

        public static void RemoteConfigUpdated()
        {
            Data.Configs.InitWithRemote(SayKit.gameConfig);
        }
    }
}
