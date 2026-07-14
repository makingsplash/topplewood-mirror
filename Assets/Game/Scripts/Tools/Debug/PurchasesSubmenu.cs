using DynamicDebugMenu.Elements;
using SGS.Extensions;

#if SAYKIT_PURCHASING
using System;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Infrastructure.Tools.Extensions;
using SGS.Store.Purchases;
using SGS.Store.Purchases.Data;
using UnityEngine.Purchasing;
// IAP 5.x ввёл UnityEngine.Purchasing.PurchaseService — устраняем неоднозначность.
using PurchaseService = SGS.Store.Purchases.PurchaseService;
#endif



namespace Services.DebugTools.Submenu
{
    public static class PurchasesSubmenu
    {
        private static DDMLabel _testStatusLabel;
        private static bool _testInProgress;
        private static string _testStep;

        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Purchases", () => context.Main.CreatePage("Purchases", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
#if SAYKIT_PURCHASING
            var debugInterface = GlobalService.PurchaseService.Debug;

            DDMCheckbox.Create(context, "Autocomplete purchases", () => debugInterface.AutocompletePurchases,
                v =>
                {
                    debugInterface.AutocompletePurchases = v;
                });

            var purchaseResults = Enum.GetNames(typeof(PurchaseService.PurchaseResult)).ToList();
            var failureReasons = Enum.GetNames(typeof(PurchaseFailureReason)).ToList();
            
            DDMDropdown.Create(context, "Autocomplete result:", () => purchaseResults, 
                () => purchaseResults.IndexOf(debugInterface.AutocompleteResult.ToString()), 
                i => debugInterface.AutocompleteResult = Enum.Parse<PurchaseService.PurchaseResult>(purchaseResults[i]));
            
            DDMDropdown.Create(context, "Autocomplete failure:", () => failureReasons,
                () => failureReasons.IndexOf(debugInterface.AutocompleteFailureReason.ToString()), 
                i => debugInterface.AutocompleteFailureReason = Enum.Parse<PurchaseFailureReason>(failureReasons[i])
            );

            DDMButton.Create(context, "Complete deferred purchases", () => debugInterface.CompleteDeferredPurchases());
            DDMButton.Create(context, "Refund all purchases", () => debugInterface.RefundAll());
            DDMButton.Create(context, "Reset store item history", () =>
            {
                Data.Purchases.PurchaseHistory.Clear();
                Data.Purchases.PurchasedNonConsumables.Clear();
                Data.Purchases.CompletePurchases.Clear();
                Data.Purchases.AwaitingPurchases.Clear();
                GlobalService.SayKit.DisablePremium();
                debugInterface.ResetTimers();
            });
            
            DDMLabel.Create(context, $"No ads timeout left: {(Data.Purchases.NoAdsNextShowTime.Value - DateTime.Now).ToConfigString()}");
            DDMButton.Create(context, "Reset NoAds timeout", () =>
            {
                Data.Purchases.NoAdsNextShowTime.Value = DateTime.Now;
            });
            DDMButton.Create(context, "Reset NoAds offers state", () =>
            {
                Data.Purchases.NoAdsChainStepIndex.Value = 0;
                Data.Purchases.LastShownNoAdsTime.Value = DateTime.Now;
            });
            
            DDMLabel.Create(context, $"No ads offers step index: {Data.Purchases.NoAdsChainStepIndex.Value}");
            DDMLabel.Create(context, $"No ads offers step time : {(DateTime.UtcNow - Data.Purchases.LastShownNoAdsTime.Value).ToConfigString()}");
            DDMButton.Create(context, "Skip NoAds offers state +1h", () =>
            {
                Data.Purchases.LastShownNoAdsTime.Value -= TimeSpan.FromHours(1);
            });
            DDMButton.Create(context, "Skip NoAds offers state +1d", () =>
            {
                Data.Purchases.LastShownNoAdsTime.Value -= TimeSpan.FromDays(1);
            });
            
            DDMButton.Create(context, "Reset shown offers", () =>
            {
                foreach (var placement in Data.Purchases.OfferStates.Keys.ToArray())
                {
                    Data.Purchases.OfferStates[placement] = new Purchases.OffersState()
                    {
                        PurchaseCount = 0,
                        LastPurchaseLevel = 0
                    };
                }
            });

            DDMLabel.Create(context, $"Deferred purchases: {string.Join(", ",GlobalService.PurchaseService.PersistentStorage.AwaitingPurchases.Select(p => p.ProductId))}");
            DDMLabel.Create(context, $"Purchased non-consumables: {string.Join(", ",GlobalService.PurchaseService.PersistentStorage.PurchasedNonConsumables)}");
            
            var lastPurchase = Data.Purchases.LastPurchaseTime.Count > 0 ? Data.Purchases.LastPurchaseTime.Last().Value : 0;
            if (lastPurchase > 0)
            {
                DDMLabel.Create(context, $"Time after purchase: {new TimeSpan(DateTime.Now.Ticks - lastPurchase).ToConfigString()}");
            }
            else
            {
                DDMLabel.Create(context, $"Time after purchase: you had no purchases");
            }

            _testStatusLabel = DDMLabel.Create(context, "");
            if (_testInProgress)
            {
                DDMLabel.Create(context, $"NonConsumable items test: {_testStep}");
            }
#else
            DDMLabel.Create(context, $"SAYKIT_PURCHASING is disabled");
#endif
        }
    }
}
