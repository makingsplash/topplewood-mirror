#if SAYKIT_PURCHASING

using System;
using System.Collections.Generic;
using System.Linq;
using SGS.Store.Purchases.Data;
using SGS.Store.Purchases.Modules;
using SGS.Store.Purchases.PersistentStorage;
using SGS.Extensions;

#if SAY_INFRASTURCTURE
using SGS.Infrastructure.Core;
using SGS.Infrastructure.Tools.Rewards;
#endif

using Unity.Services.Core;
using Unity.Services.Core.Environments;

using UnityEngine;
using UnityEngine.Purchasing;

namespace SGS.Store.Purchases
{
    public enum StoreCardsState
    {
        Permanent,
        Timed,
        Cooldown,
        Expired,
    }

    public interface ILogger
    {
        void LogPurchaseStatus(string productId, string status);
        void LogPurchaseComplete(Product product);
        void Log(string message);
        void LogError(string message);
    }

    public interface IPurchaseProcessor
    {
        /// <summary>
        /// Эта функция должна применить все модификаторы к награде из конфига, чтобы получить награду, которую игрок на самом деле получит.
        /// Она вызывается при старте покупки ее результат сохраняется до конца покупки и передается в ProcessPurchase.
        /// </summary>
        ProductRewardData[] GetActualReward(string productId, ProductRewardData[] pack);
        /// <summary>
        /// Эта функция должна выдать игроку все награды из pack.
        /// Она не должна применять модификаторы к наградам, это делает GetActualReward.
        /// </summary>
        void ProcessPurchase(string productId, ProductRewardData[] pack, int quantity, bool restore, Action onComplete);
        void ProcessRefund(string productId, Action onComplete);
    }

    public class PurchaseService
    {
        private const string _environment = "production";
        private readonly StoreCardsState[] _acceptableToBuyStates = {StoreCardsState.Permanent, StoreCardsState.Timed};

        private readonly IPurchasesPersistentStorage _persistentStorage;
        private readonly ILogger _logger;
        private readonly Dictionary<string,ProductData> _productsData;
        private readonly ITimeProvider _timeProvider;

        private readonly StoreConfigData _storeData;
        private IPurchaseProcessor _purchaseProcessor;

        private readonly StoreStateModule _storeStateModule;
        private readonly UpdateModule _updateModule;
        private readonly PurchaseHistoryModule _purchaseHistory;
        private readonly IDebug _debugModule;

        // IAP 5.x: единая точка входа вместо IStoreController + IExtensionProvider.
        private StoreController _controller;
        private bool _storeReady;
        private bool _processorIsBusy;
        private readonly Dictionary<string, PurchaseCallback> _purchaseCallbacks = new();
        private readonly List<string> _refundsQueue = new();
        private readonly Dictionary<string,string> _storeIdToProductId = new();

        protected virtual TimeSpan CancelNoConnectionPurchasesAfter  => TimeSpan.FromDays(1);

        public enum PurchaseResult
        {
            Complete,
            Failed,
            NoConnection,
            UserCancelled,
            Deferred,
        }
        public enum PurchaseCompleteStatus
        {
            Normal,
            Restore,
            AfterFail,
        }

        public delegate void StoreReadyDelegate();
        public delegate void PurchaseCallback(PurchaseResult result,PurchaseFailureReason reason);
        public delegate void PurchaseFailedDelegate(string productId, PurchaseFailureReason reason);
        public delegate void PurchaseStartedDelegate(string productId);
        public delegate void PurchaseCompleteDelegate(string productId, int quantity, PurchaseCompleteStatus status);

        public event StoreReadyDelegate OnStoreReadyEvent;
        public event PurchaseStartedDelegate OnPurchaseStartedEvent;
        public event PurchaseFailedDelegate OnPurchaseFailedEvent;
        public event PurchaseCompleteDelegate OnPurchaseCompleteEvent;
        public event PurchaseStartedDelegate OnPurchaseDeferredEvent;
        public event PurchaseStartedDelegate OnPurchaseRefundedEvent;

        public interface IDebug
        {
            bool AutocompletePurchases { get; set; }
            PurchaseResult AutocompleteResult { get; set; }
            PurchaseFailureReason AutocompleteFailureReason { get; set; }

            void CompleteDeferredPurchases();
            void ResetTimers();
            void RefundAll();
        }


        public IPurchasesPersistentStorage PersistentStorage => _persistentStorage;
        public StoreConfigData StoreData => _storeData;

        public IReadOnlyDictionary<string, ProductData> ProductsData => _productsData;

        public SellableItemData[] AllSellableItems { get; }

        public IConditionChecker ConditionChecker;

        public IDebug Debug => _debugModule;
        public ITimeProvider TimeProvider => _timeProvider;

        public IPurchaseProcessor PurchaseProcessor => _purchaseProcessor;

        public PurchaseService(IPurchasesPersistentStorage persistentStorage, ILogger logger, ITimeProvider timeProvider, StoreConfigData storeData, PurchasesData productsData, SellableItemData[] allSellableItems)
        {
            _persistentStorage = persistentStorage;
            _logger = logger;
            _storeData = storeData;
            AllSellableItems = allSellableItems;
            _timeProvider = timeProvider;
            _productsData = productsData.Products.ToDictionary(d => d.ProductId);
            _updateModule = new UpdateModule(_persistentStorage);
            _storeStateModule = new StoreStateModule(_updateModule, _timeProvider, allSellableItems, storeData.Placements);
            _purchaseHistory = new PurchaseHistoryModule(this, _updateModule, _logger);
#if GPIE_DEVELOPMENT
            _debugModule = new DebugModule(_persistentStorage, DebugCompleteDeferred, DebugRefundAll);
#else
            _debugModule = new NoDebugModule();
#endif
            Setup();
        }

        public virtual void DeInit()
        {
            if (_controller != null)
            {
                _controller.OnProductsFetched -= OnProductsFetched;
                _controller.OnProductsFetchFailed -= OnProductsFetchFailed;
                _controller.OnStoreDisconnected -= OnStoreDisconnected;
                _controller.OnPurchasePending -= OnStorePurchasePending;
                _controller.OnPurchaseConfirmed -= OnStorePurchaseConfirmed;
                _controller.OnPurchaseFailed -= OnStorePurchaseFailed;
                _controller.OnPurchaseDeferred -= OnStorePurchaseDeferred;
                _controller.OnPurchasesFetched -= OnStorePurchasesFetched;
                _controller.OnPurchasesFetchFailed -= OnStorePurchasesFetchFailed;
            }

            _storeStateModule.DeInit();
            _updateModule.DeInit();
            _purchaseHistory.DeInit();
        }

        public void PurchaseProcessorReady(IPurchaseProcessor processor)
        {
            _purchaseProcessor = processor;
            PurchaseNext();
        }

        public void RestorePurchases(Action<bool> callback = null)
        {
            if (_controller == null || !_storeReady)
            {
                _logger.Log("IAP Not initialised yet");
                callback?.Invoke(false);
                return;
            }

            // IAP 5.x: восстановление через IPurchaseService.RestoreTransactions.
            // Восстановленные покупки приходят через OnPurchasesFetched.
            _controller.RestoreTransactions((result, error) =>
            {
                _logger.Log($"IAP Restore purchases with result: {result} {error}");
                callback?.Invoke(result);
            });
        }

        public bool PurchaseProduct(SellableItemData item, PurchaseCallback callback = null)
        {
            var cardStateData = _storeStateModule.GetCardsState(item);

            if (!_acceptableToBuyStates.Contains(cardStateData.State)
                && !(cardStateData.State == StoreCardsState.Cooldown && cardStateData.AdsLeft > 0))
            {
                _logger.Log($"IAP StartPurchase: unacceptable item state to buy: {cardStateData.State.ToString()}");
                return false;
            }

            return PurchaseProduct(item.ProductId, callback);
        }

        public bool PurchaseProduct(string productId, PurchaseCallback callback = null)
        {
            if (!_productsData.TryGetValue(productId, out var productData))
            {
                _logger.LogError($"IAP StartPurchase: no product in config: {productId}");
                return false;
            }

            var rewardPack = new IPurchasesPersistentStorage.RewardPackWithId
            {
                ProductId = productId,
                RewardPack = _purchaseProcessor.GetActualReward(productId, productData.Rewards),
                PurchaseTime = _timeProvider.Now.Ticks,
            };

            _persistentStorage.AwaitingPurchases.Add(rewardPack);

            if (callback != null)
            {
                _purchaseCallbacks[productId] = callback;
            }

            OnPurchaseStartedEvent?.Invoke(productId);
            if (productData.PurchaseType == PurchaseType.Free)
            {
                _updateModule.SetStorageDirty();
                StartFreePurchase(productId);
            }
            else
            {
                var storeId = GetStoreId(productData);
                _updateModule.SaveStorageAndContinue(() => StartPurchase(storeId));
            }

            return true;
        }

        public ProductData FindProduct(string productId)
        {
            return _productsData.GetValueOrDefault(productId);
        }

        public string GetLocalisedPrice(string productId)
        {
            if (_controller == null)
            {
                return string.Empty;
            }

            if (!_productsData.TryGetValue(productId, out var productData))
            {
                _logger.LogError($"IAP StartPurchase: no product in config: {productId}");
                return string.Empty;
            }
            string storeId = GetStoreId(productData);

            Product product = _controller.GetProductById(storeId);
            if (product == null)
            {
                return string.Empty;
            }

            if (product.metadata is null)
            {
                _logger.LogError($"GetLocalisedPrice: product.metadata is null for {storeId}");
                return string.Empty;
            }

            string price = product.metadata.localizedPriceString;

            if (String.IsNullOrEmpty(price))
            {
                _logger.LogError($"GetLocalisedPrice: price is null for {storeId}");
                return string.Empty;
            }

            if (price.Contains("₽"))
            {
                price = price.Replace("₽", "RUB");
            }

            if (price.Contains("₹"))
            {
                price = price.Replace("₹", "INR");
            }
            return price;
        }

        public class PriceInfo
        {
            public float Price;
            public string Currency;
        }
        public PriceInfo GetPriceInfo(string productId)
        {
            if (_controller == null)
            {
                return null;
            }

            if (productId == null || !_productsData.TryGetValue(productId, out var productData))
            {
                return null;
            }
            string storeId = GetStoreId(productData);

            Product product = _controller.GetProductById(storeId);
            if (product == null)
            {
                return null;
            }

            if (product.metadata is null)
            {
                return null;
            }

            return new PriceInfo
            {
                Price = (float)product.metadata.localizedPrice,
                Currency = product.metadata.isoCurrencyCode
            };
        }

        [Obsolete]
        public StoreCardsState GetCardsState(SellableItemData itemData, out TimeSpan? timer)
        {
            var cardStateData = _storeStateModule.GetCardsState(itemData);
            timer = cardStateData.Timer >= TimeSpan.Zero ? cardStateData.Timer : null;
            return cardStateData.State;
        }

        public StoreCardStateData GetCardsState(SellableItemData itemData)
        {
            return _storeStateModule.GetCardsState(itemData);
        }

        public void RecordCardSeen(string productId, bool canRepeat = false)
        {
            _storeStateModule.RecordCardSeen(productId, canRepeat);
        }

        public StoreConfigPlacementData[] GetStorePlacements()
        {
            _storeStateModule.UpdatePlacements();
            return _storeData.Placements;
        }

        public SellableItemData GetCurrentStoreItem(StoreConfigPlacementData placement)
        {
            return GetSellableItem(_storeStateModule.GetSellableItemId(placement));
        }

        public SellableItemData GetSellableItem(string itemId)
        {
            return AllSellableItems.FirstOrDefault(item => item.ItemId.Equals(itemId, StringComparison.InvariantCultureIgnoreCase));
        }

        private string GetStoreId(ProductData product)
        {
            var id = product.ProductId;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(product.AndroidId))
            {
                id = product.AndroidId;
            }
#endif
            if (Application.platform == RuntimePlatform.Android && !string.IsNullOrEmpty(product.AndroidId))
            {
                id = product.AndroidId;
            }
            if (Application.platform == RuntimePlatform.IPhonePlayer && !string.IsNullOrEmpty(product.IosId))
            {
                id = product.IosId;
            }

            return id;
        }

        private async void Setup()
        {
            // инициализация UnityServices
            try
            {
                var options = new InitializationOptions().SetEnvironmentName(_environment);
                await UnityServices.InitializeAsync(options);
            }
            catch (Exception exception)
            {
                _logger.LogError($"IAP UnityServices.Initialize exception: Message = [{exception.Message}]");
            }

            // IAP 5.x: создаём StoreController (обёртку над Store/Product/Purchase сервисами).
            _controller = UnityIAPServices.StoreController();

            _controller.OnProductsFetched += OnProductsFetched;
            _controller.OnProductsFetchFailed += OnProductsFetchFailed;
            _controller.OnStoreDisconnected += OnStoreDisconnected;
            _controller.OnPurchasePending += OnStorePurchasePending;
            _controller.OnPurchaseConfirmed += OnStorePurchaseConfirmed;
            _controller.OnPurchaseFailed += OnStorePurchaseFailed;
            _controller.OnPurchaseDeferred += OnStorePurchaseDeferred;
            _controller.OnPurchasesFetched += OnStorePurchasesFetched;
            _controller.OnPurchasesFetchFailed += OnStorePurchasesFetchFailed;

            // Pending-заказы (в т.ч. отложенные/незавершённые) приходят через OnPurchasePending при FetchPurchases.
            _controller.ProcessPendingOrdersOnPurchasesFetched(true);

            // подключение к стору
            try
            {
                await _controller.Connect();
            }
            catch (Exception exception)
            {
                _logger.LogError($"IAP Store Connect exception: Message = [{exception.Message}]");
                return;
            }

            FetchProductDefinitions();
        }

        private void FetchProductDefinitions()
        {
            var definitions = new List<ProductDefinition>();

            foreach (var productData in _productsData.Values)
            {
                string storeId = GetStoreId(productData);
                _storeIdToProductId[storeId] = productData.ProductId;

                switch (productData.PurchaseType)
                {
                    case PurchaseType.Consumable:
                        definitions.Add(new ProductDefinition(storeId, ProductType.Consumable));
                        break;
                    case PurchaseType.NonConsumable:
                        definitions.Add(new ProductDefinition(storeId, ProductType.NonConsumable));
                        break;
                }
            }

            _controller.FetchProducts(definitions);
        }

        private void StartFreePurchase(string productId)
        {
            _logger.Log("IAP Free product purchase: " + productId);
            CompletePurchase(productId, 1,false);
        }

        private void StartPurchase(string storeId)
        {
            _logger.Log("IAP StartPurchase: " + storeId);

            if (_controller == null || !_storeReady)
            {
                _logger.LogError("IAP _controller is not ready");
                HandlePurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
                return;
            }

            Product product = _controller.GetProductById(storeId);
            if (product == null)
            {
                _logger.LogError($"IAP StartPurchase: no product in store: {storeId}" );
                HandlePurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
                return;
            }
#if GPIE_DEVELOPMENT
            if (_debugModule.AutocompletePurchases)
            {
                DebugCompletePurchaseAfter(1.0f);
            }
            else
#endif
            {
                _controller.PurchaseProduct(product);
            }
        }

        // IAP 5.x: аналог старого ProcessPurchase — покупка оплачена, но не подтверждена.
        // Здесь выдаём награду и подтверждаем заказ (ConfirmPurchase = consume/acknowledge).
        private void OnStorePurchasePending(PendingOrder order)
        {
            var items = order.CartOrdered.Items();
            if (items != null)
            {
                foreach (var cartItem in items)
                {
                    ProcessPaidProduct(cartItem.Product, cartItem.Quantity);
                }
            }

            // Подтверждаем заказ, чтобы стор завершил/потребил покупку.
            _controller.ConfirmPurchase(order);
        }

        private void OnStorePurchaseConfirmed(Order order)
        {
            switch (order)
            {
                case ConfirmedOrder confirmed:
                    var items = confirmed.CartOrdered?.Items();
                    if (items != null && items.Count > 0)
                        _logger.Log($"IAP OnPurchaseConfirmed {items[0].Product.definition.id}");
                    break;
                case FailedOrder failed:
                    _logger.LogError($"IAP ConfirmPurchase failed: {failed.FailureReason} {failed.Details}");
                    break;
            }
        }

        private void OnStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            _logger.LogError($"IAP Store disconnected: {failure.Message}");
            _storeReady = false;
        }

        private void ProcessPaidProduct(Product product, int quantity)
        {
            _logger.Log($"IAP ProcessPurchase {product.definition.id} qty={quantity}");

            if (!_storeIdToProductId.TryGetValue(product.definition.id, out var productId))
            {
                _logger.LogError($"ProcessPurchase: cannot get product id from platform id {product.definition.id}, using platform id");
                productId = product.definition.id;
            }

            bool restore = _persistentStorage.AwaitingPurchases.All(p => p.ProductId != productId);

            if (!restore)
            {
                _logger.LogPurchaseComplete(product);
            }

            if (quantity <= 0)
            {
                quantity = 1;
            }

            CompletePurchase(productId, quantity, restore);
        }

        private void CompletePurchase(string productId, int quantity, bool restore)
        {
            IPurchasesPersistentStorage.RewardPackWithId awaitedPurchase = default;
            PurchaseCompleteStatus status = PurchaseCompleteStatus.Restore;
            if (!restore)
            {
                awaitedPurchase = _persistentStorage.AwaitingPurchases.First(p => p.ProductId == productId);
                _storeStateModule.RecordPurchaseTime(productId);
                status = awaitedPurchase.WasFailing ? PurchaseCompleteStatus.AfterFail : PurchaseCompleteStatus.Normal;
                _persistentStorage.AwaitingPurchases.Remove(awaitedPurchase);
                _updateModule.SetStorageDirty();
            }

            if (!_productsData.TryGetValue(productId, out var productData))
            {
                _logger.LogError($"IAP StartPurchase: no product in config: {productId}");
                return;
            }

            if (productData.PurchaseType == PurchaseType.NonConsumable)
            {
                _persistentStorage.PurchasedNonConsumables.Add(productId);
                _updateModule.SetStorageDirty();
            }

            _logger.Log("IAP Send purchase complete event");

            if (_purchaseCallbacks.ContainsKey(productId))
            {
                _purchaseCallbacks[productId]?.Invoke(PurchaseResult.Complete, PurchaseFailureReason.Unknown);
                _purchaseCallbacks.Remove(productId);
            }

            _persistentStorage.CompletePurchases.Add(
                new IPurchasesPersistentStorage.CompletePurchase
                {
                    Id = productId,
                    Quantity = quantity,
                    Restored = restore,
                    Status = status,
                    RewardPack = awaitedPurchase.RewardPack ?? productData.Rewards
                });
            _updateModule.SetStorageDirty();
            TryProcessNext();

        }

        // IAP 5.x: отложенная покупка (например, требуется родительское подтверждение).
        private void OnStorePurchaseDeferred(DeferredOrder order)
        {
            string productId = ResolveProductId(order);
            HandleDeferred(productId);
        }

        private void HandleDeferred(string productId)
        {
            _logger.Log("IAP OnDeferredPurchase");
            if (productId == null)
            {
                return;
            }

            if (_purchaseCallbacks.ContainsKey(productId))
            {
                _purchaseCallbacks[productId]?.Invoke(PurchaseResult.Deferred, PurchaseFailureReason.Unknown);
                _purchaseCallbacks.Remove(productId);
            }
            _logger.LogPurchaseStatus(productId, "deferred");
            OnPurchaseDeferredEvent?.Invoke(productId);
        }

        // IAP 5.x: неудачная покупка приходит как FailedOrder.
        private void OnStorePurchaseFailed(FailedOrder order)
        {
            string productId = ResolveProductId(order);
            HandlePurchaseFailed(productId, order.FailureReason);
        }

        private void HandlePurchaseFailed(string productId, PurchaseFailureReason reason)
        {
            _logger.LogError("IAP OnPurchaseFailed: " + reason);

            bool expectComplete = reason == PurchaseFailureReason.Unknown;

            if (productId != null)
            {
                var awaitingPurchaseIdx = _persistentStorage.AwaitingPurchases.FindIndex(r => r.ProductId == productId);

                if (awaitingPurchaseIdx < 0)
                {
                    _logger.LogError($"OnPurchaseFailed: purchase {productId} is not active");
                }
                else if (expectComplete)
                {
                    SetActivePurchaseFailed(awaitingPurchaseIdx);
                }
                else
                {
                    _persistentStorage.AwaitingPurchases.RemoveAt(awaitingPurchaseIdx);
                }

                _updateModule.SetStorageDirty();
                if (_purchaseCallbacks.ContainsKey(productId))
                {
                    _purchaseCallbacks[productId]?.Invoke(ToPurchaseResult(reason), reason);
                    _purchaseCallbacks.Remove(productId);
                }

                _logger.LogPurchaseStatus(productId, "failed");
            }
            else
            {
                if (expectComplete)
                {
                    for (int index = 0; index < _persistentStorage.AwaitingPurchases.Count; index++)
                    {
                        SetActivePurchaseFailed(index);
                    }
                }
                else
                {
                    _persistentStorage.AwaitingPurchases.Clear();
                }

                _updateModule.SetStorageDirty();
                foreach (var pair  in _purchaseCallbacks)
                {
                    pair.Value?.Invoke(ToPurchaseResult(reason), reason);
                }
                _purchaseCallbacks.Clear();
            }

            OnPurchaseFailedEvent?.Invoke(productId, reason);
        }

        private string ResolveProductId(Order order)
        {
            var items = order?.CartOrdered?.Items();
            if (items == null || items.Count == 0)
            {
                return null;
            }

            var storeId = items[0].Product.definition.id;
            if (_storeIdToProductId.TryGetValue(storeId, out var productId))
            {
                return productId;
            }

            _logger.LogError($"ResolveProductId: cannot get product id from platform id {storeId}, using platform id");
            return storeId;
        }

        private static PurchaseResult ToPurchaseResult(PurchaseFailureReason reason)
        {
           return reason switch
            {
                PurchaseFailureReason.UserCancelled => PurchaseResult.UserCancelled,
                PurchaseFailureReason.Unknown => PurchaseResult.NoConnection,
                _ => PurchaseResult.Failed
            };
        }

        private void SetActivePurchaseFailed(int index)
        {
            var awaitingPurchase = _persistentStorage.AwaitingPurchases[index];
            awaitingPurchase.WasFailing = true;
            _persistentStorage.AwaitingPurchases[index] = awaitingPurchase;
        }

        // IAP 5.x: товары получены — стор готов, цены доступны.
        private void OnProductsFetched(List<Product> products)
        {
            _logger.Log($"IAP OnProductsFetched: {products?.Count ?? 0}");
            _storeReady = true;

            CheckForStaleActivePurchases();
            OnStoreReadyEvent?.Invoke();

            // Запрашиваем уже совершённые покупки (восстановление non-consumable, выявление рефандов).
            _controller.FetchPurchases();
        }

        private void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            _logger.LogError($"IAP OnProductsFetchFailed: {failure?.FailureReason}");
        }

        // IAP 5.x: совершённые ранее покупки (в т.ч. восстановленные).
        private void OnStorePurchasesFetched(Orders orders)
        {
            _logger.Log($"IAP OnPurchasesFetched: confirmed={orders.ConfirmedOrders.Count}, pending={orders.PendingOrders.Count}");

            var ownedNonConsumables = new HashSet<string>();

            // Подтверждённые заказы — это завершённые non-consumable/подписки.
            // Pending-заказы приходят отдельно через OnPurchasePending (ProcessPendingOrdersOnPurchasesFetched=true).
            foreach (var order in orders.ConfirmedOrders)
            {
                var items = order.CartOrdered?.Items();
                if (items == null)
                {
                    continue;
                }

                foreach (var cartItem in items)
                {
                    var storeId = cartItem.Product.definition.id;
                    if (!_storeIdToProductId.TryGetValue(storeId, out var productId))
                    {
                        productId = storeId;
                    }

                    if (!_productsData.TryGetValue(productId, out var productData)
                        || productData.PurchaseType != PurchaseType.NonConsumable)
                    {
                        continue;
                    }

                    ownedNonConsumables.Add(productId);
                    // Повторно применяем restore-награду (например, NoAds). Идемпотентно.
                    ProcessPaidProduct(cartItem.Product, cartItem.Quantity);
                }
            }

            CheckForRefunds(ownedNonConsumables);
        }

        private void OnStorePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            _logger.LogError("IAP OnPurchasesFetchFailed");
        }

        private void TryProcessNext()
        {
            if (_purchaseProcessor == null || _processorIsBusy)
            {
                return;
            }

            PurchaseNext();
        }

        private void PurchaseNext()
        {
            _processorIsBusy = false;

            if (_refundsQueue.Count > 0)
            {
                _processorIsBusy = true;
                var productId = _refundsQueue[0];
                _refundsQueue.RemoveAt(0);
                _logger.LogPurchaseStatus(productId, "refunded");
                OnPurchaseRefundedEvent?.Invoke(productId);
                _logger.Log($"IAP calling ProcessRefund for {productId}");
                _purchaseProcessor.ProcessRefund(productId, PurchaseNext);
            }
            else if (_persistentStorage.CompletePurchases.Count > 0)
            {
                _processorIsBusy = true;

                var purchase = _persistentStorage.CompletePurchases[0];
                _persistentStorage.CompletePurchases.RemoveAt(0);
                _updateModule.SetStorageDirty();

                _logger.LogPurchaseStatus(purchase.Id, "completed");
                OnPurchaseCompleteEvent?.Invoke(purchase.Id, purchase.Quantity, purchase.Status);

                _logger.Log($"IAP calling ProcessPurchase for {purchase.Id}");
                _purchaseProcessor.ProcessPurchase(purchase.Id, purchase.RewardPack, purchase.Quantity, purchase.Restored, PurchaseNext);
            }
        }

        // IAP 5.x: рефанд выявляется сравнением сохранённых non-consumable с реально принадлежащими игроку
        // (старый product.hasReceipt больше не используется).
        private void CheckForRefunds(HashSet<string> ownedNonConsumables)
        {
            if (_debugModule.AutocompletePurchases)
            {
                return;
            }

            foreach (var productId in _persistentStorage.PurchasedNonConsumables)
            {
                if (ownedNonConsumables.Contains(productId))
                {
                    continue;
                }

                _logger.Log($"IAP Found refunded product {productId}");
                _refundsQueue.Add(productId);
            }

            if (_refundsQueue.Count > 0)
            {
                _persistentStorage.PurchasedNonConsumables.RemoveAll(s => _refundsQueue.Contains(s));
                _persistentStorage.SaveState();
                TryProcessNext();
            }
        }

        private void CheckForStaleActivePurchases()
        {
            List<int> toRemove = new ();
            for (int i = 0; i < _persistentStorage.AwaitingPurchases.Count; i++)
            {
                var awaitingPurchase = _persistentStorage.AwaitingPurchases[i];

                if (!awaitingPurchase.WasFailing)
                {
                    continue;
                }

                var startAgo = _timeProvider.Now - new DateTime(awaitingPurchase.PurchaseTime);
                if (startAgo < CancelNoConnectionPurchasesAfter)
                {
                    continue;
                }
                _logger.Log($"IAP Found failed purchase that was not complete after {startAgo.ToConfigString()}");
                _logger.LogPurchaseStatus(awaitingPurchase.ProductId, "discarded");
                toRemove.Add(i);
            }

            if (toRemove.Count > 0)
            {
                toRemove.Reverse();
                foreach (var i in toRemove)
                {
                    _persistentStorage.AwaitingPurchases.RemoveAt(i);
                }
                _persistentStorage.SaveState();
            }
        }

#if GPIE_DEVELOPMENT
        private float _debugCompletePurchaseTimeLeft;
        private void DebugCompletePurchaseAfter(float timeout)
        {
            _debugCompletePurchaseTimeLeft = timeout;
            _updateModule.OnUpdate += WaitForDebugCompletePurchase;
        }

        // IAP 5.x: в эдиторе результат покупки эмулируется напрямую через productId,
        // без создания фейковых Product/Order (конструкторы внутренние).
        private void WaitForDebugCompletePurchase()
        {
            _debugCompletePurchaseTimeLeft -= Time.deltaTime;
            if (_debugCompletePurchaseTimeLeft > 0)
            {
                return;
            }
            _updateModule.OnUpdate -= WaitForDebugCompletePurchase;

            if (_persistentStorage.AwaitingPurchases.Count == 0)
            {
                return;
            }

            var productId = _persistentStorage.AwaitingPurchases[^1].ProductId;

            switch (_debugModule.AutocompleteResult)
            {
                case PurchaseResult.Complete:
                    CompletePurchase(productId, 1, false);
                    break;
                case PurchaseResult.Failed:
                    HandlePurchaseFailed(productId, _debugModule.AutocompleteFailureReason);
                    break;
                case PurchaseResult.NoConnection:
                    HandlePurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
                    break;
                case PurchaseResult.UserCancelled:
                    HandlePurchaseFailed(productId, PurchaseFailureReason.UserCancelled);
                    break;
                case PurchaseResult.Deferred:
                    HandleDeferred(productId);
                    break;
            }
        }
        private void DebugCompleteDeferred()
        {
            foreach (var purchase in _persistentStorage.AwaitingPurchases.ToList())
            {
                CompletePurchase(purchase.ProductId, 1, false);
            }
        }

        private void DebugRefundAll()
        {
            foreach (var productId in _persistentStorage.PurchasedNonConsumables)
            {
                _logger.Log($"IAP Debug refund product {productId}");
                _refundsQueue.Add(productId);
                OnPurchaseRefundedEvent?.Invoke(productId);
            }
            _persistentStorage.PurchasedNonConsumables.Clear();
            _persistentStorage.SaveState();
            TryProcessNext();
        }
#endif
    }
}
#endif
