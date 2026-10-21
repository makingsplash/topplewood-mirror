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
        }

        public static void RemoteConfigUpdated()
        {
            Data.Configs.InitWithRemote(SayKit.gameConfig);
        }
    }
}
