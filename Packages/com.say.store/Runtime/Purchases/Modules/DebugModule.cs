#if SAYKIT_PURCHASING
using System;
using SGS.Store.Purchases.PersistentStorage;
using UnityEngine.Purchasing;

namespace SGS.Store.Purchases.Modules
{
    public class DebugModule : PurchaseService.IDebug
    {
        private readonly IPurchasesPersistentStorage _persistentStorage;
        private readonly Action _completeDeferredCallback;
        private readonly Action _refundAllCallback;
        private bool _autocompletePurchases;
        private PurchaseService.PurchaseResult _autocompleteResult;
        private PurchaseFailureReason _autocompleteFailureReason;

        internal DebugModule(IPurchasesPersistentStorage persistentStorage, Action completeDeferredCallback, Action refundAllCallback)
        {
#if UNITY_EDITOR
            _autocompletePurchases = true;
#endif
            _persistentStorage = persistentStorage;
            _completeDeferredCallback = completeDeferredCallback;
            _refundAllCallback = refundAllCallback;
        }
        
        public bool AutocompletePurchases
        {
            get => _autocompletePurchases;
            set => _autocompletePurchases = value;
        }

        public PurchaseService.PurchaseResult AutocompleteResult
        {
            get => _autocompleteResult;
            set => _autocompleteResult = value;
        }

        public PurchaseFailureReason AutocompleteFailureReason
        {
            get => _autocompleteFailureReason;
            set => _autocompleteFailureReason = value;
        }

        public void CompleteDeferredPurchases()
        {
            _completeDeferredCallback?.Invoke();
        }

        public void ResetTimers()
        {
            _persistentStorage.LastPurchaseTime.Clear();
            _persistentStorage.FirstSeenTime.Clear();
            _persistentStorage.SaveState();
        }

        public void RefundAll()
        {
            _refundAllCallback?.Invoke();
        }
    }

    public class NoDebugModule : PurchaseService.IDebug
    {
        public bool AutocompletePurchases
        {
            get => false;
            set {}
        }
        public PurchaseService.PurchaseResult AutocompleteResult
        {
            get => PurchaseService.PurchaseResult.Failed;
            set {}
        }
        public PurchaseFailureReason AutocompleteFailureReason
        {
            get => PurchaseFailureReason.Unknown;
            set {}
        }

        public void CompleteDeferredPurchases() {}
        public void ResetTimers() {}
        public void RefundAll() {}
    }
}
#endif