using System;
using Tools.Purchases.Data.Common;
#if SAY_INFRASTURCTURE
using SGS.Infrastructure.Tools.Rewards;
#endif

namespace SGS.Store.Purchases.Data
{
    public enum PurchaseType
    {
        Consumable = 0, 
        NonConsumable = 1,
        Free = 2,
    }
    
    [Serializable]
    public class ProductData
    {
        public string ProductId;
        public ProductRewardData[] Rewards;
        public PurchaseType PurchaseType;
        public string AndroidId;
        public string IosId;
    }

    [Serializable]
    public class PurchasesData
    {
        public ProductData[] Products;
    }
}