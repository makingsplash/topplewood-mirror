using System;

using Newtonsoft.Json;

using SGS.Infrastructure.Tools.Common;

using UnityEngine;

namespace SGS.Store.Purchases.Data
{
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class StoreConfigPlacementData
    {
        public string PlacementId;
        public string VisualBlockId;
        public int LoopStartIndex;
        public string ItemId;
        
        // Этот функционал пока не используется, поэтому спрятан для пользователя. 
        // Планируется использование в будущем, поэтому не удален.
        // При инициализации, надо создать хотя бы один степ с ItemId.
        [HideInInspector]
        public StorePlacementStepData[] Steps;
    }

    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class StorePlacementStepData
    {
        public string ItemId;
        public TimeSpanValue TimeToStep;
        public bool ShowTimer;
    }
    
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class SellableItemData
    {
        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        public class VisualSettingsData
        {
            [ProductId]
            public string IdBeforeDiscount;
            
            public string VisualId;
            public string LocalizationId;

            public string[] Badges;
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        public class LimitedAvailabilitySettingsData
        {
            public bool HasCooldown;
            public TimeSpanValue CooldownTime = new();
            public int AdsCountDuringCooldown;
            public string AdsPlacement;
        }

        public string ItemId;
        
        [ProductId]
        public string ProductId;

        public VisualSettingsData VisualSettings;
        public LimitedAvailabilitySettingsData LimitedAvailabilitySettings;
                
        public bool IsTimedOffer;
        public TimeSpanValue AccessTime = new();
        
        public string ShowCondition;
    }

    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class StoreConfigBlockData
    {
        public string VisualBlockId;
        public string ShowCondition;
    }

    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class SellableItemsConfigData
    {
        public SellableItemData[] StoreItems;
        public SellableItemData[] NonStoreItems;
        
        [NonSerialized]
        public SellableItemData[] AllSellableItems;
    }

    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class StoreConfigData
    {
        public StoreConfigPlacementData[] Placements;
        public StoreConfigBlockData[] Blocks;
    }
}