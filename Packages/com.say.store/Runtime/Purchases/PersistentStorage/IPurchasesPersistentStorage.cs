using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Tools.Purchases.Data.Common;
#if SAY_INFRASTURCTURE
using SGS.Infrastructure.Tools.Rewards;
#endif

namespace SGS.Store.Purchases.PersistentStorage
{
    public interface IPurchasesPersistentStorage
    {
        public struct CompletePurchase
        {
            public string Id;
            public int Quantity; 
            public bool Restored;
            public PurchaseService.PurchaseCompleteStatus Status;
            public ProductRewardData[] RewardPack;
        }
        
        public struct HistoryItem
        {
            public int TotalCount;
            public int RefundsCount;
            public long LastTime;
        }
        
        public struct PlacementItem
        {
            public int StepIndex;
            public long LastUpdateTime;
        }
        
        public struct CooldownItem
        {
            public long CooldownStartTime;
            public long LastUpdateTime;
            public int AdsLeft;
        }
        
        [JsonObject]
        public struct RewardPackWithId : IEquatable<RewardPackWithId>
        {
            public string ProductId;
            public ProductRewardData[] RewardPack;
            public bool WasFailing;
            public long PurchaseTime;

            public bool Equals(RewardPackWithId other)
            {
                return ProductId == other.ProductId && Equals(RewardPack, other.RewardPack);
            }

            public override bool Equals(object obj)
            {
                return obj is RewardPackWithId other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ProductId, RewardPack);
            }
        }
        
        List<RewardPackWithId> AwaitingPurchases { get; }
        List<string> PurchasedNonConsumables { get; }
        List<CompletePurchase> CompletePurchases { get; }
        Dictionary<string, HistoryItem> PurchaseHistory { get; }
        Dictionary<string, long> FirstSeenTime { get; }
        Dictionary<string, long> LastPurchaseTime { get; }
        Dictionary<string, PlacementItem> PlacementHistory { get; }
        Dictionary<string, CooldownItem> CooldownHistory { get; }

        public bool HasUpdates { get; set; }

        void SaveState();
    }
}