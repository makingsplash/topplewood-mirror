using SGS.Infrastructure.Core;
using SGS.Services.Interfaces;
using SGS.Store.Purchases.PersistentStorage;

namespace SGS.Store.Purchases
{
    public class GamePurchaseService : PurchaseService, IService
    {
        private readonly PurchasesConfig _purchasesConfig;
        private readonly StoreVisualConfig _visualConfig;
        private readonly PurchaseTrackerModule _purchaseTrackerModule;

        public GamePurchaseService(IPurchasesPersistentStorage persistentStorage,
            PurchasesConfig purchasesConfig,
            StoreConfig storeConfig,
            StoreItemsConfig storeItemsConfig,
            StoreVisualConfig visualConfig) 
            : base(persistentStorage, new GamePurchasesLogger(), SystemTimeProvider.Instance, storeConfig.ConfigData,
            purchasesConfig.Purchases, storeItemsConfig.AllSellableItems)
        {
            _purchasesConfig = purchasesConfig;
            _visualConfig = visualConfig;
            ConditionChecker = new StoreConditionChecker();
            _purchaseTrackerModule = new PurchaseTrackerModule(this);
        }
        
        public GamePurchaseService(IPurchasesPersistentStorage persistentStorage, ITimeProvider timeProvider,
            PurchasesConfig purchasesConfig,
            StoreConfig storeConfig,
            StoreItemsConfig storeItemsConfig,
            StoreVisualConfig visualConfig) 
            : base(persistentStorage, new GamePurchasesLogger(),  timeProvider, storeConfig.ConfigData,
                purchasesConfig.Purchases, storeItemsConfig.AllSellableItems)
        {
            _purchasesConfig = purchasesConfig;
            _visualConfig = visualConfig;
            ConditionChecker = new StoreConditionChecker();
            _purchaseTrackerModule = new PurchaseTrackerModule(this);
        }

        public PurchasesConfig PurchasesConfig => _purchasesConfig;

        public StoreVisualConfig VisualConfig => _visualConfig;

        public virtual int CurrentLevel()
        {
            return 0;
        }
        public void ServicesReady()
        {
            _purchaseTrackerModule.ServicesReady();
        }

        public void Init()
        {
        }

        public override void DeInit()
        {
            base.DeInit();
            _purchaseTrackerModule.DeInit();
        }
    }
}