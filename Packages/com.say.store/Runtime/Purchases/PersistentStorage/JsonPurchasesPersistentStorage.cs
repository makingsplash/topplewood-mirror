using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SGS.Store.Purchases.PersistentStorage
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonPurchasesPersistentStorage : IPurchasesPersistentStorage
    {
        [JsonProperty]
        private readonly List<IPurchasesPersistentStorage.RewardPackWithId> _awaitingPurchases = new();
        [JsonProperty]
        private List<string> _purchasedNonConsumables = new ();
        [JsonProperty]
        private List<IPurchasesPersistentStorage.CompletePurchase> _completePurchases = new();
        [JsonProperty]
        private Dictionary<string, IPurchasesPersistentStorage.HistoryItem> _purchaseHistory;
        [JsonProperty]
        private Dictionary<string, long> _firstSeenTime = new();
        [JsonProperty]
        private Dictionary<string, long> _lastPurchaseTime = new();
        [JsonProperty]
        private Dictionary<string, IPurchasesPersistentStorage.PlacementItem> _placementHistory = new();
        [JsonProperty]
        private Dictionary<string, IPurchasesPersistentStorage.CooldownItem> _cooldownHistory = new();
        [JsonProperty]
        private bool _hasUpdates;
        [JsonProperty]
        private bool _autocompletePurchases;

        private readonly Action<string> _valueSaver;

        public List<IPurchasesPersistentStorage.RewardPackWithId> AwaitingPurchases => _awaitingPurchases;
        public List<string> PurchasedNonConsumables => _purchasedNonConsumables;
        public List<IPurchasesPersistentStorage.CompletePurchase> CompletePurchases => _completePurchases;
        public Dictionary<string, IPurchasesPersistentStorage.HistoryItem> PurchaseHistory => _purchaseHistory;
        public Dictionary<string, long> FirstSeenTime => _firstSeenTime;
        public Dictionary<string, long> LastPurchaseTime => _lastPurchaseTime;
        public Dictionary<string, IPurchasesPersistentStorage.PlacementItem> PlacementHistory => _placementHistory;
        public Dictionary<string, IPurchasesPersistentStorage.CooldownItem> CooldownHistory => _cooldownHistory;

        public bool HasUpdates
        {
            get => _hasUpdates;
            set => _hasUpdates = value;
        }

        public JsonPurchasesPersistentStorage(string serializedValue, Action<string> valueSaver)
        {
            _valueSaver = valueSaver;
            LoadState(serializedValue);
        }

        private void LoadState(string value)
        {
            JsonConvert.PopulateObject(value, this);
        }
        public void SaveState()
        {
            var value = JsonConvert.SerializeObject(this);
            _valueSaver(value);
        }
    }
    
    
    
}
