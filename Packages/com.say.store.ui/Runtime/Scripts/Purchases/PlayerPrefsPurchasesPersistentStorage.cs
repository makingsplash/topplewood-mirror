using System.Collections.Generic;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Store.Purchases.PersistentStorage;
using UnityEngine;

namespace SGS.Store.Purchases
{
    public class PlayerPrefsPurchasesPersistentStorage : IPurchasesPersistentStorage
    {
        private readonly PlayerPrefsSerializableList<IPurchasesPersistentStorage.RewardPackWithId> _awaitingPurchases = new("Purchases.AwaitingPurchasesWithRewards");
        private readonly PlayerPrefsSerializableList<string> _purchasedNonConsumables = new("Purchases.PurchasedNonConsumables");
        private readonly PlayerPrefsSerializableList<IPurchasesPersistentStorage.CompletePurchase> _completePurchases = new("Purchases.CompletePurchasesWithRewards");
        private readonly PlayerPrefsSerializableDictionary<IPurchasesPersistentStorage.HistoryItem> _purchaseHistory = new("Purchases.PurchaseHistory");
        private readonly PlayerPrefsSerializableDictionary<long> _firstSeenTime = new("Purchases.FirstSeenTime");
        private readonly PlayerPrefsSerializableDictionary<long> _lastPurchaseTime = new("Purchases.LastPurchaseTime");
        private readonly PlayerPrefsSerializableDictionary<IPurchasesPersistentStorage.PlacementItem> _placementHistory = new("Purchases.PlacementHistory");
        private readonly PlayerPrefsSerializableDictionary<IPurchasesPersistentStorage.CooldownItem> _cooldownHistory = new("Purchases.CooldownHistory");
        private readonly PlayerPrefsBool _hasUpdates = new ("Purchases.HasUpdates", false);

        public List<IPurchasesPersistentStorage.RewardPackWithId> AwaitingPurchases => _awaitingPurchases.Value;

        public List<string> PurchasedNonConsumables => _purchasedNonConsumables.Value;

        public List<IPurchasesPersistentStorage.CompletePurchase> CompletePurchases => _completePurchases.Value;

        public Dictionary<string, IPurchasesPersistentStorage.HistoryItem> PurchaseHistory => _purchaseHistory.Value;

        public Dictionary<string, long> FirstSeenTime => _firstSeenTime.Value;

        public Dictionary<string, long> LastPurchaseTime => _lastPurchaseTime.Value;

        public Dictionary<string, IPurchasesPersistentStorage.PlacementItem> PlacementHistory => _placementHistory.Value;

        public Dictionary<string, IPurchasesPersistentStorage.CooldownItem> CooldownHistory => _cooldownHistory.Value;


        public bool HasUpdates
        {
            get => _hasUpdates.Value;
            set => _hasUpdates.Value = value;
        }
        
        public void SaveState()
        {
            _awaitingPurchases.ElementChanged();
            _purchasedNonConsumables.ElementChanged();
            _completePurchases.ElementChanged();
            _purchaseHistory.ElementChanged();
            _firstSeenTime.ElementChanged();
            _lastPurchaseTime.ElementChanged();
            PlayerPrefs.Save();
        }
    }
}