using System;
using System.Collections.Generic;
using SGS.Services.Interfaces;
using SGS.Services.Localization;
using UnityEngine.Purchasing;

namespace SGS.Services.SayKit
{
    public enum SourceType
    {
        Free = 1,
        Ads = 2,
        SoftCurrency = 3,
        HardCurrency = 4,
        IAP = 5
    }

    public interface ISayKitService : IService
    {
        string GetAppVersion();
        string GetIDFA();

        #region Premium

        bool IsPremium();
        void EnablePremium();
        void DisablePremium();

        #endregion

        #region GDPR

        bool IsGdprApplicable();
        void RevokeGdpr();

        #endregion

        bool IsInitialized();
        AppLanguage GetCurrentLanguage();
        void OverrideSystemLanguage(AppLanguage language);
        string GetLocalizedString(string key);
        bool HasLocalizedMessage(string key);

        void OpenSupportPage();

        #region Ads
        bool IsInterstitialAvailable(string place);
        bool IsRewardedAvailable(string place);
        bool ShowInterstitial(string place, Action onCloseCallback);
        void ShowRewarded(string place, Action<bool> onCloseCallback);
        bool ShowRateAppPopup();
        void ShowBanner();
        void HideBanner();
        float GetBackgroundBannerSize();

        delegate void RewardedStateCallback(bool shown);
        event RewardedStateCallback RewardedShownEvent;
        #endregion

        #region Analytics

        void TrackLevelStarted(string tag, int level, string extra);
        void TrackLevelStarted(string tag, int level, string extra, string extra2);
        void TrackLevelCompleted(string tag, int level, string extra);
        void TrackLevelCompleted(string tag, int level, string extra, string extra2);
        void TrackLevelFailed(string tag, int level, string extra);
        void TrackLevelFailed(string tag, int level, string extra, string extra2);
        void TrackChunkStarted(string name, int sequenceNumber, Dictionary<string, object> customData);
        void TrackChunkCompleted(Dictionary<string, object> customData);
        void TrackChunkFailed(Dictionary<string, object> customData);

        void TrackTutorialStep(string tutorialName, string stepName);
        void TrackTutorialCompleted();
        void TrackEvent(string eventName, int eventParam1, int eventParam2, string eventParam3, string eventParam4);
        void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, string eventParam3, string eventParam4);
        void TrackTagEvent(string eventName, string tag, int eventParam1, int eventParam2, int eventParam3, string eventParam4, string eventParam5);
        void TrackApplicationLoaded();
        void TrackRewardedOffer(string place);
        void TrackInterstitialOffer(string place);
        void TrackPurchase(Product purchaseEventArgs);
        void TrackScreen(string screenName);
        void TrackItem(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData);
        void TrackItemLoss(string item, int ownedItems, SourceType sourceId, Dictionary<string, object> customData);
        void TrackSoftIncome(int amount, int total, string place, string extra);
        void TrackSoftOutcome(int amount, int total, string place, string extra);
        void TrackHardIncome(int amount, int total, string place, string extra);
        void TrackHardOutcome(int amount, int total, string place, string extra);
        #endregion
    }
}
