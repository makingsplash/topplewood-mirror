using SGS.Services;
using SGS.Services.Containers;
using SGS.Services.DebugLog;
using UnityEngine.Purchasing;

namespace SGS.Store.Purchases
{
    public class GamePurchasesLogger : ILogger
    {
        public void LogPurchaseStatus(string productId, string status)
        {
            if (LocalServiceContainer.IsReady())
            {
                LocalServiceContainer.Get<PurchaseAnalytics>().SendPurchaseStatus(productId, status);
            }
        }

        public void LogPurchaseComplete(Product product)
        {
            GlobalService.SayKit.TrackPurchase(product);
        }

        public void Log(string message)
        {
            GlobalService.DebugLog.Log(message, DebugLogType.Purchases);
        }

        public void LogError(string message)
        {
            GlobalService.DebugLog.LogError(message);
        }
    }
}