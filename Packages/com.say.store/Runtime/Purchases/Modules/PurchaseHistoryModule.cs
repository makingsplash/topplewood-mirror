using System;
using SGS.Store.Purchases.PersistentStorage;

namespace SGS.Store.Purchases.Modules
{
    public class PurchaseHistoryModule
    {
        private readonly PurchaseService _service;
        private readonly UpdateModule _updateModule;
        private readonly ILogger _logger;

        public PurchaseHistoryModule(PurchaseService service, UpdateModule updateModule,  ILogger logger)
        {
            _service = service;
            _updateModule = updateModule;
            _logger = logger;
            _service.OnPurchaseCompleteEvent += OnPurchaseComplete;
            _service.OnPurchaseRefundedEvent += OnPurchaseRefunded;
        }

        public void DeInit()
        {
            _service.OnPurchaseCompleteEvent -= OnPurchaseComplete;
            _service.OnPurchaseRefundedEvent -= OnPurchaseRefunded;
        }

        private void OnPurchaseComplete(string productId, int quantity, PurchaseService.PurchaseCompleteStatus status)
        {
            
            if (!_service.PersistentStorage.PurchaseHistory.TryGetValue(productId, out var data))
            {
                data = new IPurchasesPersistentStorage.HistoryItem {TotalCount = 0, RefundsCount = 0};
            }

            data.LastTime = _service.TimeProvider.Now.Ticks;
            data.TotalCount += 1;
            _service.PersistentStorage.PurchaseHistory[productId] = data;
            _updateModule.SetStorageDirty();
        }
        
        private void OnPurchaseRefunded(string productId)
        {
            if (!_service.PersistentStorage.PurchaseHistory.TryGetValue(productId, out var data))
            {
                _logger.LogError($"PurchaseHistoryModule: unknown product {productId} refunded");
                return;
            }

            data.RefundsCount += 1;
            _service.PersistentStorage.PurchaseHistory[productId] = data;
            _updateModule.SetStorageDirty();

        }
    }
}