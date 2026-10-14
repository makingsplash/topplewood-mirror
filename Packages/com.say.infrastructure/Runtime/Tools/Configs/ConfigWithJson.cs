using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SGS.Infrastructure.Tools.Common;
using Sirenix.OdinInspector;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Configs
{
    public abstract class ConfigWithJson<TData> : ScriptableObject
    {
        private JsonSerializerSettings _jsonSettings;

        [SerializeField]
        private TData _data;

        protected JsonSerializerSettings JsonSettings
        {
            get
            {
                _jsonSettings ??= new() {Converters = InitJsonConverters()};
                return _jsonSettings;
            }
        }
        
        protected TData LocalConfig => _data;
        protected TData Config;
        
        
        public virtual void InitWithRemote(string remoteConfig)
        {
#if UNITY_EDITOR
            if (ConfigSwitcher.UseLocalConfig)
            {
                Config = _data;
                return;
            }
#elif SAYKIT_SMOKE_DISABLE_RUNTIME
            // Project edit (TASK-533): the smoke player never inits SayKit, so remote never arrives and the
            // empty-remote branch below would hand out a blank TData (null nested sections → NRE in overrides,
            // e.g. RewardedConfig.Revives). Mirror the editor's local mode: serve the asset's own data.
            Config = _data;
            return;
#endif

            if (!string.IsNullOrEmpty(remoteConfig))
            {
                LoadFromJson(remoteConfig);
            }
            else
            {
                Debug.LogError($"Error: remote config {GetType().Name} is null or empty");
                Config = Activator.CreateInstance<TData>();
            }
        }

        protected virtual void LoadFromJson(string jsonString)
        {
            try
            {
                Config = JsonConvert.DeserializeObject<TData>(jsonString, JsonSettings);
            }
            catch (JsonSerializationException ex)
            {
                Debug.LogError($"Error reading remote config {GetType().Name}: {ex.Message}");
            }
        }

        protected virtual List<JsonConverter> InitJsonConverters()
        {
            return new List<JsonConverter>
            {
                new StringEnumConverter(),
                new DateTimeValueConverter(),
                new TimespanValueConverter(),
                new TimeOfDayValueConverter()
            };
        }

#if UNITY_EDITOR

        [Button(ButtonSizes.Large, Name = "Export as JSON")]
        private void ExportAsJson()
        {
            var json = JsonConvert.SerializeObject(LocalConfig, Formatting.Indented, JsonSettings);
            
            JsonTextPopupWindow.Show(json, null);
        }
        [Button(ButtonSizes.Large, Name = "Import from JSON")]
        private void ImportAsJson()
        {
            JsonTextPopupWindow.Show("", json =>
            {
                LoadFromJson(json);
                _data = Config;
            });
        }
#endif
    }
}