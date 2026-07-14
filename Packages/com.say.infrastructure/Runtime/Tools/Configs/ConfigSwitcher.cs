using System.IO;
using UnityEngine;

namespace SGS.Infrastructure.Tools.Configs
{
    public static class ConfigSwitcher
    {
        private static bool? _useLocalConfig;

        public static bool UseLocalConfig
        {
            get
            {
#if GPIE_DEVELOPMENT && UNITY_EDITOR
                _useLocalConfig ??= LoadSettings().UseLocalConfig;
                return _useLocalConfig.Value;
#else
                return false;
#endif
            }
        }
        
#if GPIE_DEVELOPMENT && UNITY_EDITOR
        private const string _propertyFilePath = "UserSettings/ConfigSwitcher.json";
        public class Settings
        {
            public bool UseLocalConfig = true;
        }
        public static Settings LoadSettings()
        {
            if (File.Exists(_propertyFilePath))
            {
                return JsonUtility.FromJson<Settings>(File.ReadAllText(_propertyFilePath));
            }
            return new Settings();
        }
        public static void SaveSettings( Settings  settings )
        {
            File.WriteAllText( _propertyFilePath, JsonUtility.ToJson( settings, true ) );
        }
#endif        
    }
}