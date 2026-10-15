using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SGS.Data;
using SGS.Infrastructure.Tools.Configs;
using SGS.Services.DebugLog;
using SGS.Services.Localization;
using UnityEngine.Purchasing;

namespace SGS.Services.SayKit
{
    public class SayKitService : ISayKitService
    {
        public string GetAppVersion()
        {
            string appVersion = global::SayKit.remoteConfig.runtime.segment == 0 
                ? global::SayKit.runtimeInfo.version 
                : global::SayKit.runtimeInfo.version + "." + global::SayKit.remoteConfig.runtime.segment;
            return appVersion;
        }
        
        public string GetIDFA()
        {
            return global::SayKit.runtimeInfo.idfa;
        }

        public bool IsPremium()
        {
            return global::SayKit.isPremium;
        }

        public void EnablePremium()
        {
            global::SayKit.enablePremium();
        }

        public void DisablePremium()
        {
            global::SayKit.disablePremium();
        }

        public bool IsGdprApplicable()
        {
            bool? isApplicable = global::SayKit.isGdprApplicable();
            return isApplicable.HasValue && isApplicable.Value;
        }

        public void RevokeGdpr()
        {
            global::SayKit.revokeGdprConsent();
        }

        public string GetPrivacyPolicyLink()
        {
            return global::SayKit.GetPrivacyPolicyLink();
        }

        public bool IsInitialized()
        {
            return global::SayKit.initializedProgress > 0.75f;
        }

        public AppLanguage GetCurrentLanguage()
        {
            SayKitLanguage language = global::SayKit.getCurrentLanguage();
            return (AppLanguage)(int)language;
        }

        public void OverrideSystemLanguage(AppLanguage language)
        {
            SayKitLanguage skLang = (SayKitLanguage)(int)language;
            global::SayKit.overrideSystemLanguage(skLang);
        }

        public string GetLocalizedString(string key)
        {
            return global::SayKit.getLocalizedString(key);
        }

        public bool HasLocalizedMessage(string key)
        {
            return global::SayKit.hasLocalizedMessage(key);
        }

#if UNITY_EDITOR
        private static readonly SayKitGameConfig _defaultConfig = new();
#endif
        public SayKitGameConfig GetRemoteConfig()   
        {
#if UNITY_EDITOR
            if (ConfigSwitcher.UseLocalConfig)
            {
                return _defaultConfig;
            }
#endif

            return global::SayKit.gameConfig;
        }    

        public void OpenSupportPage()
        {
            global::SayKit.OpenSupportPage();
        }

        #region Ads

        public bool IsInterstitialAvailable(string place)
        {
#if !GPIE_DEVELOPMENT
            return global::SayKit.isInterstitialAvailable(place);
#else
            switch (SayData.Debug.AdMode.Value)
            {
                case DebugData.AdModes.Full:
                    return global::SayKit.isInterstitialAvailable(place);
                case DebugData.AdModes.Dev:
                case DebugData.AdModes.Silent:
                    return true;
                case DebugData.AdModes.Off:
                    return false;
            }
            return true;  
#endif
        }

        private bool _waitForRewardedAction;
        private bool _waitForRewardedActionFinished;

        public bool IsRewardedAvailable(string place)
        {
#if !GPIE_DEVELOPMENT
            return global::SayKit.isRewardedAvailable(place);
#else
            if (SayData.Debug.AdMode.Value == DebugData.AdModes.Off)
            {
                return false;
            }
            if (SayData.Debug.AdMode.Value == DebugData.AdModes.Dev)
            {
                if (_waitForRewardedActionFinished)
                {
                    return true;
                }
                if (!_waitForRewardedAction)
                {
                    _waitForRewardedAction = true;
                    GlobalService.Timer.Delay.AddListener(WaitForRewarded, 2.0f);
                }
                return false;
            } 
            return global::SayKit.isRewardedAvailable(place);
#endif
        }
        
        private void WaitForRewarded()
        {
            _waitForRewardedAction = false;
            _waitForRewardedActionFinished = true;
        }

        public bool ShowInterstitial(string place, Action onCloseCallback)
        {
#if !GPIE_DEVELOPMENT
           return global::SayKit.showInterstitial(place, onCloseCallback);
#else
            switch (SayData.Debug.AdMode.Value)
            {
                case DebugData.AdModes.Full:
                    return global::SayKit.showInterstitial(place, onCloseCallback);

                case DebugData.AdModes.Dev:
                    SayData.Debug.DebugInterstitialHandler?.Invoke(place, onCloseCallback);
                    break;

                case DebugData.AdModes.Silent:
                case DebugData.AdModes.Off:
                    onCloseCallback?.Invoke();
                    break;
            }
            return true;
#endif
        }

        public void ShowRewarded(string place, Action<bool> onCloseCallback)
        {
            void CloseCallback(bool success) 
            {
                RewardedShownEvent?.Invoke(false);
                onCloseCallback?.Invoke(success);
            };

            RewardedShownEvent?.Invoke(true);
#if !GPIE_DEVELOPMENT
           global::SayKit.showRewarded(place, CloseCallback);
           return;
#else
            switch (SayData.Debug.AdMode.Value)
            {
                case DebugData.AdModes.Full:
                    global::SayKit.showRewarded(place, CloseCallback);
                    break;

                case DebugData.AdModes.Dev:
                    _waitForRewardedActionFinished = false;
                    SayData.Debug.DebugRewardedHandler?.Invoke(place, CloseCallback);
                    break;

                case DebugData.AdModes.Silent:
                    CloseCallback(true);
                    break;
                case DebugData.AdModes.Off:
                    CloseCallback(false);
                    break;
            }
#endif
        }

        public bool ShowRateAppPopup()
        {
            return global::SayKit.showRateAppPopup();
        }
        
        public void ShowBanner()
        {
            GlobalService.DebugLog.Log($"ShowBanner", DebugLogType.Analytics);
            global::SayKit.showBanner();
        }

        public void HideBanner()
        {
            GlobalService.DebugLog.Log($"HideBanner", DebugLogType.Analytics);
            global::SayKit.hideBanner();
        }
        public float GetBackgroundBannerSize()
        {
            return  global::SayKit.GetBackgroundBannerSize();
        }

        public event ISayKitService.RewardedStateCallback RewardedShownEvent;

#endregion

        #region Analytics

        public void TrackLevelStarted(string tag, int level, string extra)
        {
            GlobalService.DebugLog.Log($"TrackLevelStarted tag: {tag}, level: {level}, extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackLevelStarted(tag, level, 0, extra);
        }

        public void TrackLevelStarted(string tag, int level, string extra, string extra2)
        {
            GlobalService.DebugLog.Log($"TrackLevelStarted tag: {tag}, level: {level}, extra: {extra}, extra2: {extra2}", DebugLogType.Analytics);
            global::SayKit.trackLevelStarted(tag, level, 0, extra, 0, extra2);
        }

        public void TrackLevelCompleted(string tag, int level, string extra)
        {
            GlobalService.DebugLog.Log($"TrackLevelCompleted tag: {tag}, level: {level}, extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackLevelCompleted(tag, level,0, extra);
        }

        public void TrackLevelCompleted(string tag, int level, string extra, string extra2)
        {
            GlobalService.DebugLog.Log($"TrackLevelCompleted tag: {tag}, level: {level}, extra: {extra}, extra2: {extra2}", DebugLogType.Analytics);
            global::SayKit.trackLevelCompleted(tag, level, 0, extra, 0, extra2);
        }

        public void TrackLevelFailed(string tag, int level, string extra)
        {
            GlobalService.DebugLog.Log($"TrackLevelFailed tag: {tag}, level: {level}, extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackLevelFailed(tag, level, 0, extra);
        }

        public void TrackLevelFailed(string tag, int level, string extra, string extra2)
        {
            GlobalService.DebugLog.Log($"TrackLevelFailed tag: {tag}, level: {level}, extra: {extra}, extra2: {extra2}", DebugLogType.Analytics);
            global::SayKit.trackLevelFailed(tag, level, 0, extra, 0, extra2);
        }

        public void TrackChunkStarted(string name, int sequenceNumber, Dictionary<string, object> customData)
        {
            GlobalService.DebugLog.Log($"TrackChunkStarted name {name}, sequenceNumber: {sequenceNumber}, customData {JsonConvert.SerializeObject(customData)}", DebugLogType.Analytics);
            global::SayKit.trackChunkStarted(name, sequenceNumber, customData);
        }

        public void TrackChunkCompleted(Dictionary<string, object> customData)
        {
            GlobalService.DebugLog.Log($"TrackChunkCompleted customData {JsonConvert.SerializeObject(customData)}", DebugLogType.Analytics);
            global::SayKit.trackChunkCompleted(customData);
        }

        public void TrackChunkFailed(Dictionary<string, object> customData)
        {
            GlobalService.DebugLog.Log($"TrackChunkFailed customData {JsonConvert.SerializeObject(customData)}", DebugLogType.Analytics);
            global::SayKit.trackChunkFailed(customData);
        }
        
        public void TrackTutorialStep(string tutorialName, string stepName)
        {
            GlobalService.DebugLog.Log($"TrackTutorialStep", DebugLogType.Analytics);
            global::SayKit.trackTutorialStep(tutorialName, stepName);
        }
        
        public void TrackTutorialCompleted()
        {
            GlobalService.DebugLog.Log($"TrackTutorialCompleted", DebugLogType.Analytics);
            global::SayKit.trackTutorialCompleted();
        }

        public void TrackEvent(string eventName, int eventParam1, int eventParam2, string eventParam3, string eventParam4)
        {
            GlobalService.DebugLog.Log($"TrackEvent eventName: {eventName}, eventParam1: {eventParam1}, eventParam2: {eventParam2} eventParam3: {eventParam3}, eventParam4: {eventParam4}", DebugLogType.Analytics);
            global::SayKit.trackEvent(eventName, eventParam1, eventParam2, eventParam3, eventParam4);
        }

        public void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, string eventParam3, string eventParam4)
        {
            GlobalService.DebugLog.Log($"TrackTagEvent eventName: {eventName}, tag: {tag} eventParam1: {eventParam1}, eventParam2: {eventParam2} eventParam3: {eventParam3}, eventParam4: {eventParam4}", DebugLogType.Analytics);
            global::SayKit.trackTagEvent(eventName, tag, eventParam1, eventParam2, eventParam3, eventParam4);
        }

        public void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, int eventParam3, string eventParam4, string eventParam5)
        {
            GlobalService.DebugLog.Log($"TrackTagEvent eventName: {eventName}, tag: {tag} eventParam1: {eventParam1}, eventParam2: {eventParam2} eventParam3: {eventParam3}, eventParam4: {eventParam4}, eventParam5: {eventParam5}", DebugLogType.Analytics);
            global::SayKit.trackTagEvent(eventName, tag, eventParam1, eventParam2, eventParam3, eventParam4, eventParam5);
        }

        public void TrackApplicationLoaded()
        {
            GlobalService.DebugLog.Log($"TrackApplicationLoaded", DebugLogType.Analytics);
            global::SayKit.trackApplicationLoaded();
        }

        public void TrackRewardedOffer(string place)
        {
            GlobalService.DebugLog.Log($"TrackRewardedOffer place: {place}", DebugLogType.Analytics);
            global::SayKit.trackRewardedOffer(place);
        }

        public void TrackInterstitialOffer(string place)
        {
            global::SayKit.trackInterstitialOffer(place);
        }

        public void TrackPurchase(Product purchaseEventArgs)
        {
#if SAYKIT_PURCHASING
            global::SayKit.trackPurchase(purchaseEventArgs);
#endif
        }

        public void TrackScreen(string screenName)
        {
            global::SayKit.trackScreen(screenName);
        }

        public void TrackItem(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData)
        {
            GlobalService.DebugLog.Log($"TrackItem item: {item}, ownedItems: {ownedItems}, sourceId: {sourceId}", DebugLogType.Analytics);
            global::SayKit.trackItem(item, ownedItems, (SayKitInternal.SourceType)sourceId, customData);
        }

        public void TrackItemLoss(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData)
        {
            GlobalService.DebugLog.Log($"TrackItemLoss item: {item}, ownedItems: {ownedItems}, sourceId: {sourceId}", DebugLogType.Analytics);
            global::SayKit.trackItemLoss(item, ownedItems, (SayKitInternal.SourceType)sourceId, customData);
        }

        public void TrackSoftIncome(int amount, int total, string place, string extra)
        {
            GlobalService.DebugLog.Log($"TrackSoftIncome amount: {amount}, total: {total}, place: {place} extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackSoftIncome(amount,total, place, extra);
        }

        public void TrackSoftOutcome(int amount, int total, string place, string extra)
        {
            GlobalService.DebugLog.Log($"TrackSoftOutcome amount: {amount}, total: {total}, place: {place} extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackSoftOutcome(amount,total, place, extra);
        }

        
        public void TrackHardIncome(int amount, int total, string place, string extra)
        {
            GlobalService.DebugLog.Log($"TrackHardIncome amount: {amount}, total: {total}, place: {place} extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackHardIncome(amount, total, place, extra);
        }

        public void TrackHardOutcome(int amount, int total, string place, string extra)
        {
            GlobalService.DebugLog.Log($"TrackHardOutcome amount: {amount}, total: {total}, place: {place} extra: {extra}", DebugLogType.Analytics);
            global::SayKit.trackHardOutcome(amount, total, place, extra);
        }

#endregion

        public void Init()
        {
        }

        public void DeInit()
        {
        }
    }
}
