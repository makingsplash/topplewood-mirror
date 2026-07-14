using System;
using SGS.Services.DebugLog;
using SGS.Services.Interfaces;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SGS.Services.Date_Time
{
    public class TimerService : IService
    {
        public readonly TimerUpdate Update = new();
        public readonly TimerUpdate LateUpdate = new();
        public readonly TimerDelay Delay = new();
        public readonly TimerInterval Interval = new();
        public readonly TimerRunOnMainThread RunOnMainThread = new();
        
        protected virtual string TimerName => "Timer";

        private bool _applicationPaused = false;

        public bool IsApplicationPaused => _applicationPaused;
        
        public event Action<bool>  ApplicationPaused;

        public void Init()
        {
            var timerObject = new GameObject(TimerName);
            var timerMonoBehaviour = timerObject.AddComponent<TimerMonoBehaviour>();
            timerMonoBehaviour.Init(this);
            
            Object.DontDestroyOnLoad(timerObject);
        }

        public void DeInit() { }
        
        public virtual void OnUpdate()
        {
            Update.Invoke();
            Delay.Invoke();
            Interval.Invoke();
            RunOnMainThread.Invoke();
        }

        public virtual void OnLateUpdate()
        {
            LateUpdate.Invoke();
        }
        
        public virtual void SetPause(bool pause)
        {
            if (_applicationPaused != pause)
            {
                _applicationPaused = pause;
                ApplicationPaused?.Invoke(pause);
            }
        }

        public void LogListenersCount()
        {
            int updateListenersCount = Update.GetListenersCount();
            int lateUpdateListenersCount = LateUpdate.GetListenersCount();
            int delayListenersCount = Delay.GetListenersCount();
            int intervalListenersCount = Interval.GetListenersCount();
            
            GlobalService.DebugLog.Log($"UPDATE: {updateListenersCount}, LATE UPDATE: {lateUpdateListenersCount}, DELAY: {delayListenersCount}, INTERVAL: {intervalListenersCount}.", DebugLogType.Profiling);
        }
    }
}