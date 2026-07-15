using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.McpBridge.Handlers
{
    // Buffers Unity log messages so an MCP client can read recent console output
    // (compile/runtime errors, warnings, logs). Buffer is cleared on domain reload.
    [InitializeOnLoad]
    public static class ConsoleHandler
    {
        private const int Capacity = 1000;
        private static readonly LinkedList<LogEntry> _buffer = new();
        private static readonly object _lock = new();

        private class LogEntry
        {
            public string type;
            public string message;
            public string stackTrace;
        }

        static ConsoleHandler()
        {
            Application.logMessageReceivedThreaded += OnLog;
            McpBridgeServer.RegisterHandler("get_console_logs", GetLogs);
            McpBridgeServer.RegisterHandler("clear_console_buffer", ClearBuffer);
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _buffer.AddLast(new LogEntry { type = type.ToString(), message = message, stackTrace = stackTrace });
                while (_buffer.Count > Capacity) _buffer.RemoveFirst();
            }
        }

        // count: how many most-recent entries to return (default 50).
        // type: optional filter — Error | Assert | Warning | Log | Exception.
        private static object GetLogs(JObject p)
        {
            var count = p["count"]?.ToObject<int>() ?? 50;
            var typeFilter = p["type"]?.ToString();

            lock (_lock)
            {
                IEnumerable<LogEntry> entries = _buffer;
                if (!string.IsNullOrEmpty(typeFilter))
                    entries = entries.Where(e => e.type == typeFilter);
                return entries
                    .Reverse()
                    .Take(count)
                    .Select(e => new { type = e.type, message = e.message, stackTrace = e.stackTrace })
                    .ToArray();
            }
        }

        private static object ClearBuffer(JObject p)
        {
            lock (_lock) { _buffer.Clear(); }
            return new { cleared = true };
        }
    }
}
