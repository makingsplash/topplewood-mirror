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
#endif
            ApplyRemote(remoteConfig);
        }

        // Project edit (TASK-064): a missing or unreadable remote section serves the ASSET's data, not a blank
        // new TData() (the template's behaviour: every section of a freshly registered app played on C# zero
        // defaults, and null nested objects threw in the overrides — RewardedConfig.Revives — on a boot step the
        // player runs without try/catch). The remote is NOT merged over the asset: a section is a whole blob
        // (publisher contract, A/B controls rely on it). One line per section says whose data plays.
        private void ApplyRemote(string remoteConfig)
        {
            if (string.IsNullOrEmpty(remoteConfig))
            {
                Debug.LogWarning($"[config] {GetType().Name}: remote empty → local asset");
                Config = _data;
                return;
            }

            if (!TryLoadFromJson(remoteConfig))
            {
                Debug.LogError($"[config] {GetType().Name}: remote invalid → local asset");
                Config = _data;
            }
        }

        // Project edit (TASK-064): was `void LoadFromJson` catching only JsonSerializationException — a malformed
        // string (JsonReaderException) escaped it, and a failure left Config as it was (null on the first boot).
        // Any parse failure is caught here: the input comes from the server, and the caller falls back to the asset.
        // A literal "null" payload deserializes to null and counts as a failure too.
        protected virtual bool TryLoadFromJson(string jsonString)
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<TData>(jsonString, JsonSettings);
                if (parsed == null)
                    return false;

                Config = parsed;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading remote config {GetType().Name}: {ex.GetType().Name}: {ex.Message}");
                return false;
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
                if (TryLoadFromJson(json))
                    _data = Config;
            });
        }
#endif
    }
}