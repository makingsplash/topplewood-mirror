using System;
using SGS.Infrastructure.Tools.Observable;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using PlayerPrefsAdMode = SGS.Infrastructure.Tools.PlayerPrefsWrapper.PlayerPrefsEnum<SGS.Data.DebugData.AdModes>;

namespace SGS.Data
{
    public class DebugData
    {
        public enum AdModes
        {
            Full = 0,
            Dev = 1,
            Silent = 2,
            Off = 3
        }
        
        public readonly Observable<bool> DebugMenuShown = new(false);
        
        public readonly PlayerPrefsBool ShowMenuButton = new ("DebugData.ShowMenuButton", true);
        public readonly PlayerPrefsInt MenuButtonX = new ("DebugData.MenuButtonX", 120);
        public readonly PlayerPrefsInt MenuButtonY = new ("DebugData.MenuButtonY", 140);
        
        public readonly PlayerPrefsInt DebugLogType = new ("DebugData.DebugLogType", 0);
        public readonly PlayerPrefsBool DisableTutorial = new ("DebugData.DisableTutorial", false);
        public readonly PlayerPrefsBool DisableMerge = new ("DebugData.DisableMerge", false);
        
        public readonly PlayerPrefsAdMode AdMode = new ("DebugData.AdMode", AdModes.Full);

        public bool DisableSave = false;
        
        public Action<string, Action<bool>> DebugRewardedHandler;
        public Action<string, Action> DebugInterstitialHandler;
    }
}