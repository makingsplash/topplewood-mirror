using System;
using System.Collections.Generic;

namespace SGS.Services.Date_Time
{
    public class TimerRunOnMainThread
    {
        private class UpdateAction
        {
            public bool IsEmpty;
            public Action Action;
        }
        
        private readonly List<UpdateAction> _updateActions = new();

        public void AddListener(Action action)
        {
            lock (_updateActions)
            {
                foreach (var updateAction in _updateActions)
                {
                    if (updateAction.IsEmpty)
                    {
                        updateAction.Action = action;
                        updateAction.IsEmpty = false;
                        return;
                    }
                }
                
                _updateActions.Add(new UpdateAction
                {
                    Action = action,
                    IsEmpty = false
                });
            }
        }
    
        public void Invoke()
        {
            lock (_updateActions)
            {
                int countActions = _updateActions.Count;
                for (int i = 0; i < countActions; i++)
                {
                    var action = _updateActions[i];
                    if (action.IsEmpty)
                    {
                        continue;
                    }

                    action.Action.Invoke();
                    action.Action = null;
                    action.IsEmpty = true;
                }
            }
        }

        public void DeInit()
        {
            lock (_updateActions)
            {
                _updateActions.Clear();
            }
        }
    }
}
