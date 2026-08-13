using DataStorage;
using SayKitInternal;
using UnityEngine;

namespace Infrastructure.Loading
{
    public static class SayKitInit
    {
        // TASK-134 (owner 16:11): this is NO LONGER a [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] entry.
        // It is invoked explicitly from the boot — LoadingGame.StartAsync, guarded by !SayKit.isInitialized.
        // Why: from BeforeSceneLoad the play-loop is not spinning yet, and SayKit's editor bridge deadlocks
        // the main thread on a BLOCKING SKThreadService.RunOnMainThreadWithResult (owner captured it with
        // `sample`: -[EditorApplication TickTimer] → __psynch_cvwait, 1883/2211 samples). With the main thread
        // parked no frames are pumped, so Addressables delivery and every bounded editor timeout die and the
        // prod boot hangs forever — the day-long stall of this task. The prod studio project (hexa-sorter)
        // does exactly this: the [RuntimeInitializeOnLoadMethod] attribute is commented out on Init() and the
        // method is called from the loading scene's Awake. We mirror that from our VContainer boot entry.
        public static void Init()
        {
#if SAYKIT_SMOKE_DISABLE_RUNTIME && !UNITY_EDITOR
            // Player-only smoke stub (TASK-063 / TD-001): this project has no registered SayKit app and ships
            // no matching Firebase config. On the smoke APK SayKit's Android entry points (ContentProvider +
            // bridge activity) are stripped from the manifest (see Editor/SayKitSmokeBuildFixup.cs), so with
            // the Java bridge never initialized SayKit.init() would throw
            // NoClassDefFoundError(saygames.bridge.unity.SayKitBridge) every frame and the scene never renders
            // (black screen, ~74 err/s measured). Neutralize init in the PLAYER only.
            //
            // In the EDITOR the real init below now runs — safe again because it is called from the boot
            // (play-loop already spinning), not from BeforeSceneLoad. The editor still has no own app key
            // (TASK-064), so init won't reach EInitState.Done; LoadingGame's bounded (3s) editor waits time
            // out and the boot continues on LOCAL config. TASK-129 had widened this stub to the editor too
            // (dropping `&& !UNITY_EDITOR`) to dodge the block; with the real fix (moving the call out of
            // BeforeSceneLoad) the editor no longer needs the stub, so it returns to player-only.
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
