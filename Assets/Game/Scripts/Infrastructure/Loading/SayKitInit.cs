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
