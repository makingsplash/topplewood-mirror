using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Services.LogService
{
    [CreateAssetMenu(menuName = "WoolSort/Logging/LogServiceConfig", fileName = "LogServiceConfig", order = 0)]
    public sealed class LogServiceConfig : ScriptableObject
    {
        [Serializable]
        public struct CategoryMinLevel
        {
            public LogCategory Category;
            public LogLevel MinLevel;
        }

        [field: SerializeField] public LogLevel DevelopmentMinLevel { get; private set; } = LogLevel.Info;
        [field: SerializeField] public LogLevel ProductionMinLevel { get; private set; } = LogLevel.Warning;
        [field: SerializeField] public List<CategoryMinLevel> CategoryOverrides { get; private set; } = new();
    }
}
