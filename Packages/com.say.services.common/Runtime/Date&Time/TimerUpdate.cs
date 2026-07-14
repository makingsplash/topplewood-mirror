using System;
using System.Collections.Generic;

namespace SGS.Services.Date_Time
{
    public class TimerUpdate
    {
        private class UpdateAction
        {
            public bool IsEmpty;
            public Action Action;
        }
        
        private readonly List<UpdateAction> _updateActions = new List<UpdateAction>();
        
        public void AddListener(Action action)
        {
            foreach (UpdateAction updateAction in _updateActions)
            {
                if (updateAction.IsEmpty)
                {
                    updateAction.Action = action;
                    updateAction.IsEmpty = false;
                    return;
                }
            }
            
            _updateActions.Add(new UpdateAction { Action = action, IsEmpty = false });
        }
        
        public void RemoveListener(Action action)
        {
            foreach (UpdateAction updateAction in _updateActions)
            {
                if (updateAction.Action == action)
                {
                    updateAction.Action = null;
                    updateAction.IsEmpty = true;
                }
            }
        }

        public void Invoke()
        {
            int countActions = _updateActions.Count;
            for (int i = 0; i < countActions; i++)
            {
                UpdateAction action = _updateActions[i];
                if (action.IsEmpty)
                {
                    continue;
                }

                action.Action.Invoke();
            }
        }

        public void Clear()
        {
            foreach (UpdateAction action in _updateActions)
            {
                action.Action = null;
                action.IsEmpty = true;
            }
            _updateActions.Clear();
        }
        
        public void DeInit()
        {
            Clear();
        }

        public int GetListenersCount()
        {
            int result = 0;
            foreach (UpdateAction updateAction in _updateActions)
            {
                if (updateAction.IsEmpty == false)
                {
                    result++;
                }
            }

            return result;
        }
    }
}
