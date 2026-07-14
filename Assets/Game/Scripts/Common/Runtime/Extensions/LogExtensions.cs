using System;
using UnityEngine;

namespace Common.Extensions
{
    public static class LogExtensions
    {
        public static IDisposable DebugLogScope(this string message) =>
            new WrapLogScope(message, "START: ", "END: ");

        public static IDisposable DebugLogScope(this string message, string startPrefix) =>
            new WrapLogScope(message, startPrefix);

        public static IDisposable DebugLogScope(this string message, string startPrefix, string endPrefix) =>
            new WrapLogScope(message, startPrefix, endPrefix);
    }

    public sealed class WrapLogScope : IDisposable
    {
        private readonly string _msg;
        private readonly string _startPrefix;
        private readonly string _endPrefix;

        public WrapLogScope(string msg, string startPrefix = null, string endPrefix = null)
        {
            _msg = msg;
            _startPrefix = startPrefix;
            _endPrefix = endPrefix;
#if DEBUG
            // TODO: Включить для логирования
            // Debug.Log($"{_startPrefix}{_msg}");
#endif
        }

        public void Dispose()
        {
#if DEBUG
            // Debug.Log($"{_endPrefix}{_msg}");
#endif
        }
    }
}
