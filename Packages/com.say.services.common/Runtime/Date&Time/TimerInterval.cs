using System;
using System.Collections.Generic;
using UnityEngine;

namespace SGS.Services.Date_Time
{
    public class TimerInterval
    {
        private class IntervalAction
        {
            public bool IsEmpty;
            public Action Action;
            public float Time;
            public float Interval;
        }

        private readonly List<IntervalAction> _intervalActions = new List<IntervalAction>();
        
        
        public void AddListener(Action action, float interval)
        {
            foreach (IntervalAction intervalAction in _intervalActions)
            {
                if (intervalAction.IsEmpty)
                {
                    intervalAction.Action = action;
                    intervalAction.Time = interval;
                    intervalAction.Interval = interval;
                    intervalAction.IsEmpty = false;
                    return;
                }
            }

            _intervalActions.Add(new IntervalAction { Action = action, Time = interval, Interval = interval, IsEmpty = false });
        }
        
        public void RemoveListener(Action action)
        {
            foreach (IntervalAction intervalAction in _intervalActions)
            {
                if (intervalAction.Action == action)
                {
                    intervalAction.Action = null;
                    intervalAction.IsEmpty = true;
                }
            }
        }

        public void Invoke()
        {
            float deltaTime = Time.deltaTime;
            int countActions = _intervalActions.Count;
            for (int i = 0; i < countActions; i++)
            {
                IntervalAction action = _intervalActions[i];
                if (action.IsEmpty)
                {
                    continue;
                }

                action.Time -= deltaTime;

                if (action.Time <= 0.0f)
                {
                    action.Action.Invoke();
                    action.Time += action.Interval;
                }
            }
        }

        public void DeInit()
        {
            Clear();
        }
        
        public void Clear()
        {
            foreach (var intervalAction in _intervalActions)
            {
                intervalAction.Action = null;
                intervalAction.IsEmpty = true;
            }
            _intervalActions.Clear();
        }
        
        public int GetListenersCount()
        {
            int result = 0;
            foreach (IntervalAction intervalAction in _intervalActions)
            {
                if (intervalAction.IsEmpty == false)
                {
                    result++;
                }
            }

            return result;
        }
    }
}
