using System;
using System.Collections.Generic;
using SGS.Services.Localization;
using UnityEngine;
using UnityEngine.Purchasing;

namespace SGS.Services.SayKit
{
    public class EmptySayKitService : ISayKitService
    {
        
        public string GetIDFA()
        {
            return "00000000-0000-0000-0000-000000000000";
        }

        public string GetAppVersion()
        {
            return string.Empty;
        }

        public bool IsPremium()
        {
            return false;
        }

        public void EnablePremium()
        {
        }

        public void DisablePremium()
        {
        }

        public bool IsGdprApplicable()
        {
            return false;
        }

        public void RevokeGdpr()
        {
        }

        public bool IsInitialized()
        {
            return true;
        }

        public AppLanguage GetCurrentLanguage()
        {
            return (AppLanguage)PlayerPrefs.GetInt("SayKitWrapper.AppLanguage", (int)AppLanguage.English);
        }

        public void OverrideSystemLanguage(AppLanguage language)
        {
            PlayerPrefs.SetInt("SayKitWrapper.AppLanguage", (int)language);
        }

        public string GetLocalizedString(string key)
        {
            return key;
        }

        public bool HasLocalizedMessage(string key)
        {
            return false;
        }
        public SayKitGameConfig GetRemoteConfig()
        {
            return new SayKitGameConfig();
        }

        public void OpenSupportPage()
        {
        }

        #region Ads

        public bool IsInterstitialAvailable(string place)
        {
            return true;
        }

        public bool IsRewardedAvailable(string place)
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

        public bool ShowInterstitial(string place, Action onCloseCallback)
        {
            GlobalService.Timer.Delay.AddListener(onCloseCallback, 0.5f);
            return true;
        }

        public void ShowRewarded(string place, Action<bool> onCloseCallback)
        {
            RewardedShownEvent?.Invoke(true);
            _waitForRewardedActionFinished = false;
            GlobalService.Timer.Delay.AddListener(() =>
            {
                onCloseCallback(true);
                RewardedShownEvent?.Invoke(false);
            }, 0.5f);
        }

        public float GetBackgroundBannerSize()
        {
            return 100.0f;
        }

        public event ISayKitService.RewardedStateCallback RewardedShownEvent;

#endregion

        #region Analytics

        public void TrackLevelStarted(string tag, int level, string extra)
        {
        }

        public void TrackLevelStarted(string tag, int level, string extra, string extra2)
        {
        }

        public void TrackLevelCompleted(string tag, int level, string extra)
        {
        }

        public void TrackLevelCompleted(string tag, int level, string extra, string extra2)
        {
        }

        public void TrackLevelFailed(string tag, int level, string extra)
        {
        }

        public void TrackLevelFailed(string tag, int level, string extra, string extra2)
        {
        }

        public void TrackChunkStarted(string name, int sequenceNumber, Dictionary<string, object> customData)
        {
        }

        public void TrackChunkCompleted(Dictionary<string, object> customData)
        {
        }

        public void TrackChunkFailed(Dictionary<string, object> customData)
        {
        }

        public void TrackTutorialStep(string tutorialName, string stepName)
        {
        }

        public void TrackTutorialCompleted()
        {
        }
        public void TrackEvent(string eventName, int eventParam1, int eventParam2, string eventParam3, string eventParam4)
        {
        }

        public void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, string eventParam3, string eventParam4)
        {
        }
        
        public void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, int eventParam3, string eventParam4, string eventParam5)
        {
        }

        public void TrackApplicationLoaded()
        {
        }

        public void TrackRewardedOffer(string place)
        {
        }

        public void TrackInterstitialOffer(string place)
        {
        }

        public void TrackPurchase(Product purchaseEventArgs)
        {
        }

        public void TrackScreen(string screenName)
        {
        }

        public void TrackItem(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData)
        {
        }

        public void TrackItemLoss(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData)
        {
        }

        public void TrackSoftIncome(int amount, int total, string place, string extra)
        {
        }

        public void TrackSoftOutcome(int amount, int total, string place, string extra)
        {
        }

        public void TrackHardIncome(int amount, int total, string place, string extra)
        {
        }

        public void TrackHardOutcome(int amount, int total, string place, string extra)
        {
        }

#endregion

        private void WaitForRewarded()
        {
            _waitForRewardedAction = false;
            _waitForRewardedActionFinished = true;
        }

        public bool ShowRateAppPopup()
        {
            return true;
        }

        public void ShowBanner()
        {
        }

        public void HideBanner()
        {
        }

        private bool _waitForRewardedAction;
        private bool _waitForRewardedActionFinished;
        
        public void Init()
        {
        }

        public void DeInit()
        {
        }
    }
}