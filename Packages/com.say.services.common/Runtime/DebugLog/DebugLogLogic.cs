using System;
using Debug = UnityEngine.Debug;

namespace SGS.Services.DebugLog
{
    public class DebugLogLogic : IDebugLogLogic
    {
        private DebugLogType _mask;

        public DebugLogLogic(DebugLogType mask)
        {
            _mask = mask;
        }

        public void SetMask(DebugLogType mask)
        {
            _mask = mask;
        }
        
        public void Log(string message, DebugLogType logType)
        {
            if ((_mask & logType) != DebugLogType.None)
            {
                DateTime time = DateTime.Now;
                Debug.Log($"[{time:T}.{time.Millisecond.ToString()}] {message}");
            }                
        }

        public void Log(Func<string> messageFunc, DebugLogType logType)
        {
            if ((_mask & logType) != DebugLogType.None)
            {
                Debug.Log(messageFunc.Invoke());
            } 
        }

        public void LogError(string message)
        {
            DateTime time = DateTime.Now;
            Debug.LogError($"[{time:T}.{time.Millisecond.ToString()}] {message}");
        }
    }
}