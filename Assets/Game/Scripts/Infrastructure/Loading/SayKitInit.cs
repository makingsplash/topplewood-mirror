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
            // TASK-063 / TASK-134 (owner-approved local smoke build): this project has no registered SayKit
            // app and ships no matching Firebase config. Neutralize the BeforeSceneLoad init entry in BOTH
            // the player and the editor — the define lives in the Android scripting-defines list and the
            // editor's active build target IS Android, so it is active in-editor too.
            //
            // On device (Java player): SayKit's Android entry points (ContentProvider + bridge activity) are
            // stripped from the smoke APK's manifest (see Editor/SayKitSmokeBuildFixup.cs and TECH_DEBT
            // TD-001), so with the Java bridge never initialized this SayKit.init() throws
            // NoClassDefFoundError(saygames.bridge.unity.SayKitBridge) / UninitializedPropertyAccessException
            // every frame and the scene never renders (black screen, ~74 err/s measured).
            //
            // In the EDITOR the problem is different and just as fatal: SayKit.init() BLOCKS the main thread.
            // TASK-134 (owner, 15:38) captured the stalling main thread with `sample` —
            //   -[EditorApplication TickTimer] → _pthread_cond_wait → __psynch_cvwait  (1883/2211 samples)
            // SayKit's editor bridge dispatches init work through SKThreadService.RunOnMainThreadWithResult,
            // a BLOCKING wait for a delegate to run on the main thread; init is entered from
            // [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] with part of the work handed to a
            // coroutine/thread, and the two deadlock. With the main thread parked, no play-loop frames are
            // pumped, so Addressables delivery stops after the startup burst and none of the bounded editor
            // timeouts (they tick on that same dead loop) ever fire — the prod boot hangs forever. TASK-129
            // had removed this stub in-editor (`&& !UNITY_EDITOR`), which is exactly what introduced the
            // block; TASK-134 restores it. The bounded editor waits in LoadingGame then time out on
            // GetInitState==None and the boot continues on LOCAL config — the intended editor smoke path.
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
