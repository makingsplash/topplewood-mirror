using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using SGS.Services;
using SGS.Services.Containers;
using SGS.Services.Interfaces;

namespace SGS.Store.Purchases
{
    public enum OpenStoreType
    {
        Manual = 1,
        Force = 2
    }
    
   
    public class PurchaseAnalytics : IService
    {
        private string _storeSource;
        private OpenStoreType _storeOpenType;
        private string _currentScreen;

        public enum IapResultType
        {
            Cancelled = 0,
            Success = 1,
            NoConnection = 2,
            Other = 3,
            Restore = 4,
            SuccessAfterFailed = 16
        }
        
        
        public void Init()
        {
            Service.UI.TopScreenChangedEvent += OnScreenChanged;
        }

        public void DeInit()
        {
            Service.UI.TopScreenChangedEvent -= OnScreenChanged;
        }
        
        public void OnScreenChanged(string panelName)
        {
            // if (panelName == Screens.StoreScreen)
            // {            
            //     _storeSource ??= _currentScreen;
            //     GlobalService.SayKit.TrackEvent("shop", (int)_storeOpenType, 0, _storeSource, string.Empty);
            //     return;
            // }

            if (_storeSource != null)
            {
                _storeSource = null;
                _storeOpenType = OpenStoreType.Force;
            }
            _currentScreen = panelName;
        }
        public void TrackStoreOpenButton(string source)
        {
            _storeOpenType = OpenStoreType.Manual;
            _storeSource = source;
        }
        
        public void SendIapOfferEvent(string productId)
        {
            SendAnalyticsEventAsync("iap_offer", productId, _storeOpenType, 0, _storeSource ?? _currentScreen);
        }
        
        public void SendIapAttemptEvent(string productId)
        {
            SendAnalyticsEventAsync("iap_attempt", productId, _storeOpenType, 0, _storeSource ?? _currentScreen);
        }
        
        public void SendIapResultEvent(string productId, IapResultType resultType)
        {
            string source = resultType == IapResultType.Restore ? "restore" : _storeSource ?? _currentScreen;
            if (source == string.Empty)
            {
                source = "MainScreen";
            }
            
            SendAnalyticsEventAsync("iap_result", productId, _storeOpenType, resultType,  source);
        }

        private async void SendAnalyticsEventAsync(string eventName, string productId, OpenStoreType storeOpenType, IapResultType resultType, string source)
        {
            string extra = null;
            await UniTask.WhenAny(
                UniTask.WaitUntil(() =>
                {
                    extra = GetProductExtra(productId);
                    return extra != null;
                }),
                UniTask.WaitForSeconds(2f));

            int result = resultType == IapResultType.SuccessAfterFailed ? (int)IapResultType.Success : (int)resultType;
            GlobalService.SayKit.TrackTagEvent(eventName, productId,(int)storeOpenType, result, resultType == IapResultType.SuccessAfterFailed ? 1 : 0, source, extra);
        }

        public void SendPurchaseStatus(string productId, string status)
        {
            Dictionary<string, object> extra = new()
            {
                { "bundle_id", productId },
                { "status", status }
            };
            GlobalService.SayKit.TrackEvent("purchase_status",0,0, JsonConvert.SerializeObject(extra), string.Empty); 
        }

        private string GetProductExtra(string productId)
        {
            var price = GlobalServiceContainer.Get<GamePurchaseService>().GetPriceInfo(productId);

            if (price == null)
            {
                return string.Empty;
            }
            
            Dictionary<string, object> extra = new()
            {
                { "price", price.Price },
                { "currency", price.Currency }
            };
            
            return JsonConvert.SerializeObject(extra);
        }

    }
}