using System;
using System.Collections.Generic;
using DataStorage;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Services.LogService
{
    public sealed class UnityLogService : ILogService
    {
        private readonly WrapperUnityLogService _inner = new(Debug.unityLogger);

        public void Log(LogLevel level, string message, LogCategory? category = null, Object context = null) =>
            _inner.Log(level, message, category, context);

        public void LogException(Exception exception, string message = null, LogCategory? category = null, Object context = null) =>
            _inner.LogException(exception, message, category, context);

        public bool IsEnabled(LogLevel level, LogCategory? category = null) =>
            _inner.IsEnabled(level, category);

        public void Log(string message) => _inner.Log(message);
        public void Log(string message, Object context) => _inner.Log(message, context);
        public void LogWarning(string message) => _inner.LogWarning(message);
        public void LogWarning(string message, Object context) => _inner.LogWarning(message, context);
        public void LogError(string message) => _inner.LogError(message);
        public void LogError(string message, Object context) => _inner.LogError(message, context);
    }
    
    public sealed class WrapperUnityLogService : ILogService
    {
        private static readonly object RuntimeFilterLock = new();
        private static readonly HashSet<LogCategory> DisabledRuntimeCategories = new();

        private readonly ILogger _logger;
        private readonly Dictionary<LogCategory, LogLevel> _categoryLevels = new();
        private LogServiceConfig _resolvedConfig;

        public static void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            if (category == LogCategory.None)
                return;

            lock (RuntimeFilterLock)
            {
                if (enabled)
                    DisabledRuntimeCategories.Remove(category);
                else
                    DisabledRuntimeCategories.Add(category);
            }
        }

        public static bool IsCategoryEnabled(LogCategory category)
        {
            if (category == LogCategory.None)
                return true;

            lock (RuntimeFilterLock)
                return !DisabledRuntimeCategories.Contains(category);
        }

        public static void ApplyCategoryMask(int mask)
        {
            if (mask < 0)
                return;

            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                if (category == LogCategory.None)
                    continue;

                var enabled = (mask & (1 << (int)category)) != 0;
                SetCategoryEnabled(category, enabled);
            }
        }

        public WrapperUnityLogService(ILogger logger) =>
            _logger = CreateLogger(logger);

        private static ILogger CreateLogger(ILogger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            return logger;
        }

        public void Log(LogLevel level, string message, LogCategory? category = null, Object context = null)
        {
            if (!IsEnabled(level, category))
                return;

            var formatted = FormatMessage(message, category);

            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Info:
                    _logger.Log(LogType.Log, message: formatted, context: context);
                    break;
                case LogLevel.Warning:
                    _logger.Log(LogType.Warning, message: formatted, context: context);
                    break;
                case LogLevel.Error:
                    _logger.Log(LogType.Error, message: formatted, context: context);
                    break;
                default:
                    _logger.Log(LogType.Log, message: formatted, context: context);
                    break;
            }
        }

        public void LogException(Exception exception, string message = null, LogCategory? category = null, Object context = null)
        {
            if (exception == null)
                return;

            if (!string.IsNullOrWhiteSpace(message))
                Log(LogLevel.Error, message, category, context);

            if (IsEnabled(LogLevel.Error, category))
                _logger.LogException(exception, context);
        }

        public bool IsEnabled(LogLevel level, LogCategory? category = null)
        {
            if (category.HasValue && !IsCategoryEnabled(category.Value))
                return false;

            var minLevel = ResolveMinLevel(category);
            return level >= minLevel;
        }

        public void Log(string message) => Log(LogLevel.Info, message);
        public void Log(string message, Object context) => Log(LogLevel.Info, message, context: context);

        public void LogWarning(string message) => Log(LogLevel.Warning, message);
        public void LogWarning(string message, Object context) => Log(LogLevel.Warning, message, context: context);

        public void LogError(string message) => Log(LogLevel.Error, message);
        public void LogError(string message, Object context) => Log(LogLevel.Error, message, context: context);

        private LogLevel ResolveMinLevel(LogCategory? category)
        {
            if (category == LogCategory.None)
                category = null;

            var config = ResolveConfig();

            if (category.HasValue && _categoryLevels.TryGetValue(category.Value, out var value))
                return value;

            if (config != null)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return config.DevelopmentMinLevel;
#else
                return config.ProductionMinLevel;
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return LogLevel.Info;
#else
            return LogLevel.Warning;
#endif
        }

        private LogServiceConfig ResolveConfig()
        {
            if (!Data.IsReady())
                return null;

            var config = Data.Configs.LogServiceConfig;
            if (ReferenceEquals(config, _resolvedConfig))
                return config;

            _resolvedConfig = config;
            _categoryLevels.Clear();

            if (config == null)
                return null;

            foreach (var rule in config.CategoryOverrides)
            {
                if (rule.Category == LogCategory.None)
                    continue;

                _categoryLevels[rule.Category] = rule.MinLevel;
            }

            return config;
        }

        private static string FormatMessage(string message, LogCategory? category)
        {
            if (!category.HasValue || category.Value == LogCategory.None)
                return message;

            return $"[{category}] {message}";
        }
    }

    public static class StaticLogService
    {
        private static readonly ILogService UnityFallback = new WrapperUnityLogService(Debug.unityLogger);

        public static ILogService Current { get; private set; } = UnityFallback;

        public static void Set(ILogService logService)
        {
            Current = logService ?? UnityFallback;
        }
    }
}
