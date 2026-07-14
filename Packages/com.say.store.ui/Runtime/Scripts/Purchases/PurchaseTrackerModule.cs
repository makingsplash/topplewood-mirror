using System;
using System.Collections.Generic;
using SGS.Services.Containers;
using UnityEngine.Purchasing;

namespace SGS.Store.Purchases
{
    public class PurchaseTrackerModule
    {
        private PurchaseService _service;
        private readonly List<Action> _deferredAnalytics = new();

        public PurchaseTrackerModule(PurchaseService service)
        {
            _service = service;
            _service.OnPurchaseStartedEvent += PurchaseStartedEvent;
            _service.OnPurchaseFailedEvent += PurchaseFailedEvent;
            _service.OnPurchaseCompleteEvent += PurchaseCompleteEvent;
        }
        
        public void DeInit()
        {
            _service.OnPurchaseStartedEvent -= PurchaseStartedEvent;
            _service.OnPurchaseFailedEvent -= PurchaseFailedEvent;
            _service.OnPurchaseCompleteEvent -= PurchaseCompleteEvent;
        }
        
        public void ServicesReady()
        {
            foreach (var action in _deferredAnalytics)
            {
                action();
            }
            _deferredAnalytics.Clear();
        }
        
        private bool CanSendEvent()
        {
            return LocalServiceContainer.IsReady() && LocalServiceContainer.Get<PurchaseAnalytics>() != null;
        }
        private void PurchaseStartedEvent(string productId)
        {
            if (CanSendEvent())
            {
                LocalServiceContainer.Get<PurchaseAnalytics>().SendIapAttemptEvent(productId);
            }
            else
            {
                _deferredAnalytics.Add(() => LocalServiceContainer.Get<PurchaseAnalytics>().SendIapAttemptEvent(productId));
            }
        }

        private void PurchaseFailedEvent(string productId, PurchaseFailureReason reason)
        {
            PurchaseAnalytics.IapResultType resultType = reason switch
            {
                PurchaseFailureReason.ProductUnavailable => productId == null
                    ? PurchaseAnalytics.IapResultType.NoConnection
                    : PurchaseAnalytics.IapResultType.Other,
                PurchaseFailureReason.UserCancelled => PurchaseAnalytics.IapResultType.Cancelled,
                PurchaseFailureReason.Unknown => PurchaseAnalytics.IapResultType.NoConnection,
                _ => PurchaseAnalytics.IapResultType.Other
            };
            if (CanSendEvent())
            {
                LocalServiceContainer.Get<PurchaseAnalytics>().SendIapResultEvent(productId, resultType);
            }
            else
            {
                _deferredAnalytics.Add(() =>  LocalServiceContainer.Get<PurchaseAnalytics>().SendIapResultEvent(productId, resultType));
            }
        }

        private void PurchaseCompleteEvent(string productId, int quantity, PurchaseService.PurchaseCompleteStatus status)
        {
            PurchaseAnalytics.IapResultType resultType = status switch
            {
                PurchaseService.PurchaseCompleteStatus.Normal => PurchaseAnalytics.IapResultType.Success,
                PurchaseService.PurchaseCompleteStatus.AfterFail => PurchaseAnalytics.IapResultType.SuccessAfterFailed,
                _ => PurchaseAnalytics.IapResultType.Restore,
            };
            if (CanSendEvent())
            {
                LocalServiceContainer.Get<PurchaseAnalytics>().SendIapResultEvent(productId, resultType);
            }
            else
            {
                _deferredAnalytics.Add(() =>  LocalServiceContainer.Get<PurchaseAnalytics>().SendIapResultEvent(productId, resultType));
            }
        }
    }
}