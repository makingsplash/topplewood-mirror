using System;

namespace SGS.Services.DebugLog
{
    public class DebugLogEmptyLogic : IDebugLogLogic
    {
        public void SetMask(DebugLogType mask)
        {
            
        }
        
        public void Log(string message, DebugLogType logType)
        {
            
        }

        public void Log(Func<string> messageFunc, DebugLogType logType)
        {
            
        }

        public void LogError(string message)
        {
            
        }
    }
}