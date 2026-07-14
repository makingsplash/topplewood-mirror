using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Profiling;

namespace Diagnostics
{
    public sealed class Diagnostics : IDiagnostics
    {
        private readonly IObjectPool<Stopwatch> _stopwatchPool = new ObjectPool<Stopwatch>(createFunc: CreateStopwatch, actionOnRelease: ReleaseStopwatch, defaultCapacity: 0);
        private readonly ILogger _logger;

        private int _scopeLevel = -1;
        
        public Diagnostics(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable CreateScope(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Cannot be null or empty", nameof(name));
            
            return new Scope(this, name);
        }

        #region Stopwatch pool

        private static Stopwatch CreateStopwatch() => 
            new();

        private static void ReleaseStopwatch(Stopwatch stopwatch)
        {
            stopwatch.Stop();
            stopwatch.Reset();
        }

        #endregion

        private sealed class Scope : IDisposable
        {
            private readonly Diagnostics _diagnostics;
            private readonly string _name;
            private readonly Stopwatch _stopwatch;
            
            private bool _disposed;

            public Scope(Diagnostics diagnostics, string name)
            {
                _diagnostics = diagnostics;
                _name = name;

                var stopwatchPool = diagnostics._stopwatchPool;
                _stopwatch = stopwatchPool.Get();
                _stopwatch.Start();
#if DEBUG
                Profiler.BeginSample(name);
#endif
                
                _diagnostics._scopeLevel++;
            }

            public void Dispose()
            {
                if(_disposed)
                    return;
                
                _stopwatch.Stop();

                var logger = _diagnostics._logger;
                if (logger.IsLogTypeAllowed(LogType.Log))
                    logger.Log($"Stopwatch \"{_name}\". Level: {_diagnostics._scopeLevel.ToString()}; Elapsed time: {_stopwatch.ElapsedMilliseconds.ToString()}ms;");
                
                var stopwatchPool = _diagnostics._stopwatchPool;
                stopwatchPool.Release(_stopwatch);
#if DEBUG
                Profiler.EndSample();
#endif

                _diagnostics._scopeLevel--;
                
                _disposed = true;
            }
        }
    }
}
