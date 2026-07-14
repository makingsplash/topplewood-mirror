
using System;

using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
#if SAYKIT_PURCHASING
using SGS.Infrastructure.Tools.Observable;
using SGS.Store.Purchases;
#endif

namespace DataStorage
{
#if SAYKIT_PURCHASING
    public class Purchases : PlayerPrefsPurchasesPersistentStorage
    {
        public readonly PlayerPrefsSerializableList<string> OldAwaitingPurchases = new("Purchases.AwaitingPurchases");
        
        public struct OffersState
        {
            public int LastPurchaseLevel;
            public int PurchaseCount;
            public int SessionsToLowerOffer;
            public string CurrentProductId;
        }
        public readonly PlayerPrefsSerializableDictionary<OffersState> OfferStates = new("Purchases.OfferStates");
        public readonly Observable<DateTime> NoAdsNextShowTime = new (DateTime.MinValue);
        
        public readonly PlayerPrefsInt NoAdsChainStepIndex = new("Purchases.NoAdsChainStepIndex", 0);
        public readonly PlayerPrefsDateTime LastShownNoAdsTime = new PlayerPrefsDateTime("Purchases.LastShownNoAdsTime");
    }
#endif
}
