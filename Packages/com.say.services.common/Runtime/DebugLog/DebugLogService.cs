using System;
using System.Collections.Generic;
using System.Diagnostics;

using SGS.PureInterfaces;
using SGS.Services.Interfaces;

namespace SGS.Services.DebugLog
{
    /// <summary>
    /// Сервис который обрабатывает и фильтрует все логи, прежде чем выводить их в консоль
    /// </summary>
    public class DebugLogService: IService
    {
        private IDebugLogLogic _logic;
        
        private Dictionary<DebugLogType, ILogger> _loggers = new();

        public DebugLogService(IDebugLogLogic logic)
        {
            _logic = logic;
        }

        public void Init() { }

        public void DeInit() { }

        public void SetDebugLogic(IDebugLogLogic logic)
        {
            _logic = logic;
        }

        public void SetMask(DebugLogType mask)
        {
            _logic.SetMask(mask);
        }

        [Conditional("GPIE_DEVELOPMENT")]
        public void Log(string message, DebugLogType logType)
        {
            _logic.Log(message, logType);
        }

        [Conditional("GPIE_DEVELOPMENT")]
        public void Log(Func<string> messageFunc, DebugLogType logType)
        {
            _logic.Log(messageFunc, logType);
        }

        public void LogError(string message)
        {
            _logic.LogError(message);
        }

        public ILogger GetLogger(DebugLogType logType)
        {
            if (!_loggers.ContainsKey(logType))
            {
                _loggers.Add(logType, new InnerLogger(new WeakReference<DebugLogService>(this), logType));
            }
            
            return _loggers[logType];
        }
        
        
        private class InnerLogger : ILogger
        {
            private readonly WeakReference<DebugLogService> _logServiceReference;
            private readonly DebugLogType _logType;

            public InnerLogger(WeakReference<DebugLogService> logServiceReference, DebugLogType logType)
            {
                _logServiceReference = logServiceReference;
                _logType = logType;
            }
            
            public void Log(string message)
            {
#if GPIE_DEVELOPMENT                
                if (_logServiceReference.TryGetTarget(out DebugLogService logService))
                {
                    logService.Log(message, _logType);
                }
#endif                
            }

            public void LogError(string message)
            {
                if (_logServiceReference.TryGetTarget(out DebugLogService logService))
                {
                    logService.LogError(message);
                }
            }
        }
    }
}