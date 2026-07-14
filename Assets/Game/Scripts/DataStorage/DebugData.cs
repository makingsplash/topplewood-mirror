using System;
using System.Collections.Generic;
// using Gameplay.HexaSort.Generator.PileCreators;

using SGS.Infrastructure.Tools.Observable;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;


#if GPIE_DEVELOPMENT
using PlayerPrefsAdMode = SGS.Infrastructure.Tools.PlayerPrefsWrapper.PlayerPrefsEnum<DataStorage.DebugData.AdModes>;
#else
using PlayerPrefsBool = SGS.Infrastructure.Tools.PlayerPrefsWrapper.DebugPrefsDummyValue<bool>;
using PlayerPrefsInt = SGS.Infrastructure.Tools.PlayerPrefsWrapper.DebugPrefsDummyValue<int>;
using PlayerPrefsString = SGS.Infrastructure.Tools.PlayerPrefsWrapper.DebugPrefsDummyValue<string>;
using PlayerPrefsAdMode = SGS.Infrastructure.Tools.PlayerPrefsWrapper.DebugPrefsDummyValue<DataStorage.DebugData.AdModes>;
using PlayerPrefsTimeSpan = SGS.Infrastructure.Tools.PlayerPrefsWrapper.DebugPrefsDummyValue<System.TimeSpan>;
#endif

namespace DataStorage
{
    public class DebugData
    {
        public const string PrefsTag = "DebugData";

        public enum AdModes
        {
            Full = 0,
            Dev = 1,
            Silent = 2,
            Off = 3,
        }
        
        public readonly Observable<bool> DebugMenuShown = new(false);

        public bool LoadOverrideLevel = false;
        public readonly PlayerPrefsString OverrideLevel = new ("DebugData.OverrideLoadLevel", string.Empty, new(PrefsTag));

        public readonly PlayerPrefsBool ShowMenuButton = new ("DebugData.ShowMenuButton", true, new(PrefsTag));
        public readonly PlayerPrefsInt MenuButtonX = new ("DebugData.MenuButtonX", 120, new(PrefsTag));
        public readonly PlayerPrefsInt MenuButtonY = new ("DebugData.MenuButtonY", 140, new(PrefsTag));
        
        // public readonly PlayerPrefsInt DebugLogType = new ("DebugData.DebugLogType", (int)Services.DebugLog.DebugLogType.Default, new(PrefsTag));

        public readonly PlayerPrefsBool FastAnimations = new ("DebugData.FastAnimations", false, new(PrefsTag));
        
        public readonly PlayerPrefsBool DisableSave = new ("DebugData.DisableSave", false, new(PrefsTag));
        public readonly PlayerPrefsBool DebugPremium = new ("DebugData.DebugPremium", false, new(PrefsTag));

        public readonly PlayerPrefsAdMode AdMode = new ("DebugData.AdMode", AdModes.Dev, new(PrefsTag));

        public readonly PlayerPrefsString OverrideVisualConfig = new ("DebugData.OverrideVisualConfig", string.Empty, new(PrefsTag));
        public readonly PlayerPrefsString OverrideUiConfig = new ("DebugData.OverrideUiConfig", string.Empty, new(PrefsTag));
        public readonly PlayerPrefsString OverrideStoreVisualConfig = new ("DebugData.OverrideStoreVisualConfig", string.Empty, new(PrefsTag));

        public readonly PlayerPrefsBool CreativeMode = new ("DebugData.CreativeMode", false, new(PrefsTag));
        public readonly PlayerPrefsBool HideTopPanel = new ("DebugData.HideTopPanel", false, new(PrefsTag));
        
        public readonly PlayerPrefsInt CreatePileButtonX = new ("DebugData.CreatePileButtonX", 120, new(PrefsTag));
        public readonly PlayerPrefsInt CreatePileButtonY = new ("DebugData.CreatePileButtonY", 140, new(PrefsTag));

//         public AutoplayData Autoplay =
// #if GPIE_DEVELOPMENT
//             new();
// #else
//             null;
// #endif
        public Action<string, Action<bool>> DebugRewardedHandler;
        public Action<string, Action> DebugInterstitialHandler;
        
        public readonly PlayerPrefsBool ShowGeneratorInfo = new ("DebugData.ShowGeneratorInfo", false, new(PrefsTag));
        public readonly PlayerPrefsBool GeneratorDifficultyEnabled = new ("DebugData.GeneratorDifficultyEnabled", true, new(PrefsTag));
        public readonly List<string> GeneratorDebugData = new();
        public Action GeneratorDebugDataUpdated;
        // public PileCreatorBase LastPileCreator;
        
        public readonly PlayerPrefsBool ShowAutoplayPanel = new ("DebugData.ShowAutoplayPanel", false, new(PrefsTag));
        public readonly PlayerPrefsInt AutoplayPanelX = new ("DebugData.AutoplayPanelX", 0, new(PrefsTag));
        public readonly PlayerPrefsInt AutoplayPanelY = new ("DebugData.AutoplayPanelY", 0, new(PrefsTag));

        public readonly PlayerPrefsBool ShowFpsInfo = new ("DebugData.ShowFpsInfo", false, new(PrefsTag));
        public readonly PlayerPrefsBool ShowGameplayInfoPanel = new ("DebugData.ShowGameplayInfoPanel", false, new(PrefsTag));

        public readonly PlayerPrefsBool IgnoreRefreshRules = new ("DebugData.IgnoreRefreshRules", false, new(PrefsTag));

        public readonly PlayerPrefsBool AutocompletePurchases = new ("DebugData.AutocompletePurchases", false, new(PrefsTag));

        public readonly PlayerPrefsTimeSpan RaceEventTimeOffset = new ("DebugData.RaceEventTimeOffset", TimeSpan.Zero, new(PrefsTag));
        public readonly PlayerPrefsTimeSpan OfferChainTimeOffset = new ("DebugData.OfferChainTimeOffset", TimeSpan.Zero, new(PrefsTag));
        
        public readonly PlayerPrefsBool WaveHexesOnPileDrop = new ("DebugData.WaveHexesOnPileDrop", false, new(PrefsTag));
        
        public readonly PlayerPrefsBool SetChipMultiplierValue = new ("DebugData.SetChipMultiplierValue", false, new(PrefsTag));
        public readonly PlayerPrefsInt ChipMultiplierValue = new ("DebugData.ChipMultiplierValue", 0, new(PrefsTag));
        public readonly PlayerPrefsBool ShowChipMultiplierInfo = new ("DebugData.ShowChipMultiplierInfo", false, new(PrefsTag));
        
        public readonly PlayerPrefsBool ShowPlacePilePanel = new ("DebugData.ShowPlacePilePanel", false, new(PrefsTag));

        public readonly PlayerPrefsBool LavaEventAlwaysShowProgressAnimation = new ("DebugData.LavaEventAlwaysShowProgressAnimation", false, new(PrefsTag));
        public readonly PlayerPrefsBool LavaEventAlwaysShowLoseAnimation = new ("DebugData.LavaEventAlwaysShowLoseAnimation", false, new(PrefsTag));
        public readonly PlayerPrefsBool LavaEventAlwaysShowWinScreen = new ("DebugData.LavaEventAlwaysShowWinScreen", false, new(PrefsTag));
        public readonly PlayerPrefsBool LavaEventAlwaysShowFindPlayersScreen = new ("DebugData.LavaEventAlwaysShowFindPlayersScreen", false, new(PrefsTag));

        public readonly PlayerPrefsBool AllowSingleLevelRestartPanel = new ("DebugData.AllowSingleLevelRestartPanel", false, new(PrefsTag));
        
        public readonly PlayerPrefsBool CloudSaveAlwaysCompareSaves = new ("DebugData.CloudSaveAlwaysCompareSaves", false, new(PrefsTag));

        public readonly PlayerPrefsInt DebugLogType = new ("DebugData.DebugLogType", 0);
        public readonly PlayerPrefsInt LogCategoryMask = new ("DebugData.LogCategoryMask", -1);
        public readonly PlayerPrefsBool DisableTutorial = new ("DebugData.DisableTutorial", false);
        public readonly PlayerPrefsBool DisableMerge = new ("DebugData.DisableMerge", false);
        public readonly PlayerPrefsBool DisableSingleTileColumnHelper = new ("DebugData.DisableSingleTileColumnHelper", false);
        public readonly PlayerPrefsInt XRayActivationTypeOverride = new ("DebugData.XRayActivationTypeOverride", -1, new(PrefsTag));
        
    }


}
