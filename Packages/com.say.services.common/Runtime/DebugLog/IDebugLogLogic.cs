using System;

namespace SGS.Services.DebugLog
{
    public interface IDebugLogLogic
    {
        void SetMask(DebugLogType mask);
        void Log(string message, DebugLogType logType);
        void Log(Func<string> messageFunc, DebugLogType logType);
        void LogError(string message);
    }
}