using System;
using DataStorage;
using SGS.Services;
using SGS.Services.Interfaces;
using UnityEngine;

namespace Services
{
    public class HeartsService : IService
    {
        public void Init()
        {
            UpdateEnergy();
            GlobalService.Timer.Interval.AddListener(UpdateEnergy, 1f);
        }

        public void DeInit()
        {
            GlobalService.Timer.Interval.RemoveListener(UpdateEnergy);
        }

        public bool EnableHearts() => Data.Configs.HeartConfig.EnableHearts;
        public bool HasEnergy() => Data.Meta.HeartsAmount.Value > 0;
        public bool IsMaxHearts() =>  Data.Meta.HeartsAmount.Value >= Data.Configs.HeartConfig.MaxHeartsCount;
        public bool IsInfinityHearts() => Data.Meta.InfiniteHeartsExpireDate.Value > DateTime.Now;
        public bool EnoughHearts() => Data.Meta.HeartsAmount.Value != 0;
        public TimeSpan InfinityHeartsTimeLeft()
        {
            TimeSpan timeLeft = Data.Meta.InfiniteHeartsExpireDate.Value - DateTime.Now;
            if (timeLeft < TimeSpan.Zero)
            {
                timeLeft = TimeSpan.Zero;
            }
            return timeLeft;
        }
        
        public TimeSpan TimeToNextUpdate()
        {
            var updateSpan = DateTime.Now - Data.Meta.LastHeartsAddTime.Value;

            return TimeSpan.FromMinutes(Data.Configs.HeartConfig.RechargeHeartTimeMinutes) - updateSpan;
        }

        public void RechargeOneHeart()
        {
            Data.Meta.HeartsAmount.Value = Mathf.Clamp(Data.Meta.HeartsAmount.Value + 1, 0, Data.Configs.HeartConfig.MaxHeartsCount);
            Data.Meta.LastHeartsAddTime.Value = DateTime.Now;
        }

        public void AddInfinityHeartsDuration(TimeSpan value)
        {
            DateTime expireDate = Data.Meta.InfiniteHeartsExpireDate.Value; 
            if (expireDate < DateTime.Now)
            {
                expireDate = DateTime.Now;
            }

            Data.Meta.InfiniteHeartsExpireDate.Value = expireDate + value;
            
            if (!IsMaxHearts())
            {
                Data.Meta.HeartsAmount.Value = Data.Configs.HeartConfig.MaxHeartsCount;    
            }
        }
        
        public void SpendHeart()
        {
            if (IsMaxHearts())
            {
                Data.Meta.LastHeartsAddTime.Value = DateTime.Now;
            }

            Data.Meta.HeartsAmount.Value = Mathf.Clamp(Data.Meta.HeartsAmount.Value -1, 0, Data.Configs.HeartConfig.MaxHeartsCount);
        }

        public void AddHearts(int value)
        {
            Data.Meta.HeartsAmount.Value = Mathf.Clamp(Data.Meta.HeartsAmount.Value + value, 0, Data.Configs.HeartConfig.MaxHeartsCount);
        }

        private void UpdateEnergy()
        {
            if (IsMaxHearts())
            {
                return;
            }

            var updateSpan = DateTime.Now - Data.Meta.LastHeartsAddTime.Value;
            var addPoints = Mathf.FloorToInt((float)updateSpan.TotalMinutes / Data.Configs.HeartConfig.RechargeHeartTimeMinutes);
            var newPoints = Mathf.Min(Data.Meta.HeartsAmount.Value + addPoints, Data.Configs.HeartConfig.MaxHeartsCount);
            var toAdd = newPoints - Data.Meta.HeartsAmount.Value;
            
            if (toAdd <= 0)
            {
                return;
            }
            
            AddHearts(toAdd);
            if (newPoints < Data.Configs.HeartConfig.MaxHeartsCount)
            {
                var lastTime = Data.Meta.LastHeartsAddTime.Value;
                Data.Meta.LastHeartsAddTime.Value = lastTime.AddMinutes(Data.Configs.HeartConfig.RechargeHeartTimeMinutes * toAdd);
            }
            else
            {
                Data.Meta.LastHeartsAddTime.Value = DateTime.Now;
            }
        }
    }
}
