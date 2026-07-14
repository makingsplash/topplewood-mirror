using SGS.Infrastructure.Tools.Observable;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;

namespace DataStorage
{
    public class RewardedStateData
    {
        public readonly Observable<bool> InterstitialsEnabled = new(true); 
        public readonly Observable<bool> NoAdOffersEnabled = new(true); 
        public readonly Observable<bool> AdBoostersEnabled = new(true);
        public readonly Observable<bool> AdHeartsEnabled = new(true);
        public readonly Observable<bool> ReviveForRewardEnabled = new(true);

        public readonly PlayerPrefsBool InterstitialWasShown = new ("RewardedState.InterstitialWasShown", false);
        public readonly PlayerPrefsInt IntersEnableLevel = new("RewardedState.IntersEnableLevel", -1);
        
        public readonly PlayerPrefsBool AdBoostersWasShown = new ("RewardedState.AdBoostersWasShown", false);
        public readonly PlayerPrefsInt AdBoostersDisableLevel = new("RewardedState.AdBoostersDisableLevel", 0);
        public readonly PlayerPrefsInt AdBoostersEnableLevel = new("RewardedState.AdBoostersEnableLevel", -1);
        
        public readonly PlayerPrefsBool AdHeartsWasShown = new ("RewardedState.AdHeartsWasShown", false);
        public readonly PlayerPrefsInt AdHeartsDisableLevel = new("RewardedState.AdHeartsDisableLevel", 0);
        public readonly PlayerPrefsInt AdHeartsEnableLevel = new("RewardedState.AdHeartsEnableLevel", -1);
    }
}
