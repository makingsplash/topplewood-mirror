using System;
using System.Collections.Generic;
using UnityEngine;

namespace SGS.Services.Date_Time
{
    public class TimerDelay
    {
        private class DelayAction
        {
            public bool IsEmpty;
            public Action Action;
            public float Time;
        }

        private readonly List<DelayAction> _delayActions = new List<DelayAction>();
        
        
        public void AddListener(Action action, float delay)
        {
            foreach (DelayAction delayAction in _delayActions)
            {
                if (delayAction.IsEmpty)
                {
                    delayAction.Action = action;
                    delayAction.Time = delay;
                    delayAction.IsEmpty = false;
                    return;
                }
            }

            _delayActions.Add(new DelayAction { Action = action, Time = delay, IsEmpty = false });
        }
        
        public void RemoveListener(Action action)
        {
            foreach (DelayAction delayAction in _delayActions)
            {
                if (delayAction.Action == action)
                {
                    delayAction.Action = null;
                    delayAction.IsEmpty = true;
                }
            }
        }

        public void Invoke()
        {
            float deltaTime = Time.deltaTime;
            int countActions = _delayActions.Count;
            for (int i = 0; i < countActions; i++)
            {
                DelayAction action = _delayActions[i];
                if (action.IsEmpty)
                {
                    continue;
                }

                action.Time -= deltaTime;

                if (action.Time <= 0.0f)
                {
                    action.Action.Invoke();
                    action.Action = null;
                    action.IsEmpty = true;
                }
            }
        }

        public void DeInit()
        {
            Clear();
        }
        
        public void Clear()
        {
            foreach (DelayAction delayAction in _delayActions)
            {
                delayAction.Action = null;
                delayAction.IsEmpty = true;
            }
            _delayActions.Clear();
        }
        
        public int GetListenersCount()
        {
            int result = 0;
            foreach (DelayAction delayAction in _delayActions)
            {
                if (delayAction.IsEmpty == false)
                {
                    result++;
                }
            }

            return result;
        }
    }
}
