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
#if SAYKIT_SMOKE_DISABLE_RUNTIME
            // TASK-063 (owner-approved local smoke build): this project has no registered SayKit app and
            // ships no matching Firebase config, and SayKit's Android entry points (ContentProvider +
            // bridge activity) are stripped from the smoke APK's manifest — see
            // Editor/SayKitSmokeBuildFixup.cs and TECH_DEBT TD-001. With the SayKit Java bridge therefore
            // never initialized, this BeforeSceneLoad call to SayKit.init() throws
            // NoClassDefFoundError(saygames.bridge.unity.SayKitBridge) / UninitializedPropertyAccessException
            // every frame, which prevents the scene from rendering (black screen, ~74 err/s measured).
            // Neutralize the runtime entry so the smoke build can open the first level.
            // This define is Android-only and editor-added (ProjectSettings → Android scripting defines):
            // it is absent on every other build target, so player, editor and simulation behaviour is
            // unchanged — the method compiles with its full body everywhere else. Reversible: drop the
            // define once the game has its own SayKit/Firebase app (TD-001).
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
