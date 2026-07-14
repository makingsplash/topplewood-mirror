using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Services.LogService
{
    public enum LogLevel
    {
        Trace = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public interface ILogService
    {
        void Log(LogLevel level, string message, LogCategory? category = null, Object context = null);
        void LogException(Exception exception, string message = null, LogCategory? category = null, Object context = null);
        bool IsEnabled(LogLevel level, LogCategory? category = null);

        void Log(string message);
        void Log(string message, Object context);
        void LogWarning(string message);
        void LogWarning(string message, Object context);
        void LogError(string message);
        void LogError(string message, Object context);
    }
}
