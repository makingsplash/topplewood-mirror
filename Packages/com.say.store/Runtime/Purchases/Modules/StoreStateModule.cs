#if SAYKIT_PURCHASING

using System;
using System.Collections.Generic;

using SGS.Store.Purchases.Data;
using SGS.Store.Purchases.PersistentStorage;
using Tools.Purchases.Data.Common;
#if SAY_INFRASTURCTURE
using SGS.Infrastructure.Core;
#endif

using UnityEngine;

namespace SGS.Store.Purchases.Modules
{
    public class StoreStateModule 
    {
        private readonly IPurchasesPersistentStorage _persistentStorage;
        private readonly UpdateModule _updateModule;
        private readonly ITimeProvider _timeProvider;
        // private readonly StoreConfigData _storeData;

        private const float _updateInterval = 1f;
        private float _updateCooldown;
        private readonly Dictionary<string, TimeSpan> _cooldownTimes = new();
        private readonly Dictionary<string, TimeSpan> _availableTimes = new();

        private readonly SellableItemData[] _storeItemsData;
        private readonly StoreConfigPlacementData[] _placements;

        public StoreStateModule(UpdateModule updateModule, ITimeProvider timeProvider, SellableItemData[] storeItems, StoreConfigPlacementData[] placements)
        {
            _persistentStorage = updateModule.PersistentStorage;
            _updateModule = updateModule;
            _timeProvider = timeProvider;
            // _storeData = storeData;
            _storeItemsData = storeItems;
            _placements = placements;

            _updateModule.OnUpdate += OnUpdate;
        }

        public void DeInit()
        {
            _updateModule.OnUpdate -= OnUpdate;        
        }

        public StoreCardStateData GetCardsState(SellableItemData itemData)
        {
            StoreCardStateData stateData = new()
            {
                State = StoreCardsState.Permanent, Timer = TimeSpan.MinValue, AdsLeft = 0,
            };
            
            if (itemData == null)
            {
                return stateData;
            }
            
            string id = itemData.ProductId;
            if (itemData.LimitedAvailabilitySettings.HasCooldown)
            {
                if (IsItemUnderCooldown(itemData))
                {
                    var cooldownItem = _persistentStorage.CooldownHistory[itemData.ProductId];
                    stateData.State = StoreCardsState.Cooldown;
                    stateData.Timer = new DateTime(cooldownItem.CooldownStartTime) +
                        itemData.LimitedAvailabilitySettings.CooldownTime - _timeProvider.Now;
                    
                    stateData.AdsLeft = cooldownItem.AdsLeft;
                    return stateData;
                }
            }

            if (itemData.IsTimedOffer)
            {
                if (_availableTimes.TryGetValue(id, out TimeSpan time))
                {
                    stateData.State = time == TimeSpan.Zero ? StoreCardsState.Expired : StoreCardsState.Timed;
                    stateData.Timer = time;
                }
            }
            
            return stateData;
        }

        public void RecordPurchaseTime(string productId)
        {
            _persistentStorage.LastPurchaseTime[productId] = _timeProvider.Now.Ticks;
            UpdateStoreItems();
            _updateModule.SetStorageDirty();
        }
        
        public void RecordCardSeen(string productId, bool canRepeat = false)
        {
            _persistentStorage.HasUpdates = false;
            
            if (_persistentStorage.FirstSeenTime.ContainsKey(productId) && !canRepeat)
            {
                return;
            }

            _persistentStorage.FirstSeenTime[productId] = _timeProvider.Now.Ticks;
            _updateModule.SetStorageDirty();
        }

        public string GetSellableItemId(StoreConfigPlacementData placement)
        {
            if (placement.Steps.Length == 1)
            {
                return placement.Steps[0].ItemId;
            }
            
            return placement.Steps[GetPlacementCycledStep(placement)].ItemId;
        }

        public void UpdatePlacements()
        {
            foreach (var placement in _placements)
            {
                UpdatePlacement(placement);
            }
        }

        private void UpdatePlacement(StoreConfigPlacementData placement)
        {
            if (placement.Steps.Length == 1)
            {
                return;
            }
            
            int placementCycledStep = GetPlacementCycledStep(placement);
            if (placement.Steps[placementCycledStep].TimeToStep <= TimeSpan.Zero)
            {
                return;
            }
            
            if (!_persistentStorage.PlacementHistory.ContainsKey(placement.PlacementId))
            {
                _persistentStorage.PlacementHistory[placement.PlacementId] = new()
                {
                    LastUpdateTime = _timeProvider.Now.Ticks,
                    StepIndex = 0,
                };
            }
            
            var placementItem = _persistentStorage.PlacementHistory[placement.PlacementId];
            var timePassed = TimeSpan.FromTicks(_timeProvider.Now.Ticks - placementItem.LastUpdateTime);
                
            while (timePassed > placement.Steps[placementCycledStep].TimeToStep)
            {
                _persistentStorage.PlacementHistory[placement.PlacementId] = new()
                {
                    LastUpdateTime = _timeProvider.Now.Ticks, 
                    StepIndex = placementItem.StepIndex + 1,
                };
                    
                timePassed -= placement.Steps[placementCycledStep].TimeToStep;
                placementCycledStep = GetPlacementCycledStep(placement);
            }
        }

        private int GetPlacementCycledStep(StoreConfigPlacementData placement)
        {
            if (_persistentStorage.PlacementHistory.TryGetValue(placement.PlacementId,
                    out IPurchasesPersistentStorage.PlacementItem placementItem))
            {
                if (placementItem.StepIndex < placement.Steps.Length)
                {
                    return placementItem.StepIndex;
                }
                
                return (placementItem.StepIndex - placement.LoopStartIndex) % (placement.Steps.Length - placement.LoopStartIndex) + placement.LoopStartIndex;
            }

            return 0;
        }

        private void OnUpdate()
        {
            _updateCooldown -= Time.deltaTime;
            if (_updateCooldown > 0)
            {
                return;
            }
            _updateCooldown = _updateInterval;
            
            UpdateStoreItems();
        }

        private void UpdateStoreItems()
        {
            bool hasUpdates = false;
            
            foreach (var card in _storeItemsData)
            {
                if (card.LimitedAvailabilitySettings.HasCooldown)
                {
                    UpdateCooldownCard(card);
                }

                if (card.IsTimedOffer)
                {
                    UpdateTimedCard(card);
                }

                var stateData = GetCardsState(card);
                if (card.LimitedAvailabilitySettings.HasCooldown 
                    && (stateData.State == StoreCardsState.Permanent ||
                        stateData.State == StoreCardsState.Cooldown && stateData.AdsLeft > 0))
                {
                    hasUpdates = true;
                }
            }
            
            _persistentStorage.HasUpdates = hasUpdates;
        }

        private void UpdateCooldownCard(SellableItemData item)
        {
            if (!_persistentStorage.LastPurchaseTime.TryGetValue(item.ProductId, out var timeTicks))
            {
                return;
            }
            
            if (!_persistentStorage.CooldownHistory.TryGetValue(item.ProductId,
                    out IPurchasesPersistentStorage.CooldownItem cooldownItem))
            {
                SetupCooldownData(item, timeTicks);
            }

            if (IsItemUnderCooldown(item))
            {
                if (timeTicks != cooldownItem.LastUpdateTime)
                {
                    cooldownItem.LastUpdateTime = timeTicks;
                    cooldownItem.AdsLeft--;
                    
                    _persistentStorage.CooldownHistory[item.ProductId] = cooldownItem;
                    _updateModule.SetStorageDirty();
                }
            }
            else if (!IsTimeInCooldownRange(item, timeTicks))
            {
                SetupCooldownData(item, timeTicks);
            }
        }

        private void SetupCooldownData(SellableItemData item, long timeTicks)
        {
            IPurchasesPersistentStorage.CooldownItem cooldownItem = new()
            {
                CooldownStartTime = timeTicks,
                LastUpdateTime = timeTicks,
                AdsLeft = item.LimitedAvailabilitySettings.AdsCountDuringCooldown,
            };
                
            _persistentStorage.CooldownHistory[item.ProductId] = cooldownItem;
            _updateModule.SetStorageDirty();
        }

        private bool IsItemUnderCooldown(SellableItemData item)
        {
            if (!_persistentStorage.CooldownHistory.TryGetValue(item.ProductId,
                    out IPurchasesPersistentStorage.CooldownItem cooldownItem))
            {
                return false;
            }
            
            long timeTicks = cooldownItem.CooldownStartTime;
            TimeSpan timeLeft = item.LimitedAvailabilitySettings.CooldownTime -
                (_timeProvider.Now - new DateTime(timeTicks));
            
            return timeLeft > TimeSpan.Zero;
        }

        private bool IsTimeInCooldownRange(SellableItemData item, long timeTicks)
        {
            if (!_persistentStorage.CooldownHistory.TryGetValue(item.ProductId,
                    out IPurchasesPersistentStorage.CooldownItem cooldownItem))
            {
                return false;
            }

            var timeDiff = item.LimitedAvailabilitySettings.CooldownTime -
                TimeSpan.FromTicks(timeTicks - cooldownItem.CooldownStartTime);
            
            return timeDiff > TimeSpan.Zero;
        }

        private void UpdateTimedCard(SellableItemData card)
        {
            if (!_persistentStorage.FirstSeenTime.TryGetValue(card.ProductId, out var timeTicks))
            {
                return;
            }

            var timeLeft = card.AccessTime - (_timeProvider.Now - new DateTime(timeTicks));

            if (timeLeft >= TimeSpan.Zero)
            {
                _availableTimes[card.ProductId] = timeLeft;
            }
            else
            {
                _availableTimes[card.ProductId] = TimeSpan.Zero;
            }
        }
    }

}
#endif