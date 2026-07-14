using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using SGS.Infrastructure.Tools.Configs;
using SGS.Services.Interfaces;

namespace SGS.Services.Localization
{
    public class LocalizationService : IService
    {
        private Dictionary<string, string> _messages;
        private CultureInfo _localCulture;
        public event Action LocaleChanged;

        private static readonly string[] _monthLocaleKeys = {
            "Month.January",
            "Month.February",
            "Month.March",
            "Month.April",
            "Month.May",
            "Month.June",
            "Month.July",
            "Month.August",
            "Month.September",
            "Month.October",
            "Month.November",
            "Month.December"
        };

        private static readonly string[] _timeUnitKeys = {
            "Time.DayShort",
            "Time.HoursShort",
            "Time.MinutesShort",
            "Time.SecondsShort",
        };
            
        private readonly Dictionary<AppLanguage, string> _languageToCulture = new()
        {
            { AppLanguage.English, "en" },
            { AppLanguage.Russian, "ru" },
            { AppLanguage.German, "de" },
            { AppLanguage.French, "fr" },
            { AppLanguage.Spanish, "es" },
            { AppLanguage.Italian, "it" },
            { AppLanguage.Portuguese, "pt" },
            { AppLanguage.Japanese, "ja" },
            { AppLanguage.Chinese, "zh" },
            { AppLanguage.Korean, "ko" },
            { AppLanguage.Arabic, "en" },
            { AppLanguage.Hindi, "en" },
            { AppLanguage.Ukrainian, "uk" },
        };



        public string GetLocalizedString(string key)
        {
            string result;
            if (_messages != null)
            {
                if (!_messages.TryGetValue(key, out result) || result == string.Empty)
                {
                    GlobalService.DebugLog.LogError($"Localization not found for key \"{key}\"");
                    result = key;
                }
            }
            else if (GlobalService.SayKit.HasLocalizedMessage(key))
            {
                result = GlobalService.SayKit.GetLocalizedString(key);
            }
            else
            {
                result = key;
            }

            result = result.Replace("\\n", "\n");
            return result;
        }

        public string GetLocalizedString(string key, params object[] values)
        {            
            string localizedString = GlobalService.Localization.GetLocalizedString(key);

            return string.Format(localizedString, values);
        }

        public string GetLocalizedDateMD(DateTime monthDayDate)
        {
            var localizedMonth = GetLocalizedString(_monthLocaleKeys[monthDayDate.Month - 1]);
            return $"{localizedMonth}, {monthDayDate.Day}";
        }
        
        public string GetLocalizedDate(DateTime date)
        {
            var localizedDate = date.ToString(_localCulture.DateTimeFormat.ShortDatePattern);
            return localizedDate;
        }

        public string GetLocalizedTimeInterval(TimeSpan timeDelta)
        {
            if (timeDelta.Days > 0)
            {
                return timeDelta.Hours == 0 ? $"{timeDelta.Days}d" : $"{timeDelta.Days}d {timeDelta.Hours}h";
            }
            if (timeDelta.Hours > 0)
            {
                return timeDelta.Minutes == 0 ? $"{timeDelta.Hours}h" : $"{timeDelta.Hours}h {timeDelta.Minutes}m";
            }
            if (timeDelta.Minutes > 0)
            {
                return timeDelta.Seconds == 0 ? $"{timeDelta.Minutes}m" : $"{timeDelta.Minutes}m {timeDelta.Seconds}s";
            }
            return $"{timeDelta.Seconds}s";
        }

        public void Init()
        {
            var systemLanguage = GlobalService.SayKit.GetCurrentLanguage();
            if (_languageToCulture.ContainsKey(systemLanguage))
            {
                _localCulture = new CultureInfo(_languageToCulture[systemLanguage]);                
            }
            else
            {
                _localCulture = new CultureInfo(_languageToCulture[AppLanguage.English]);
            }
#if UNITY_EDITOR
            if (ConfigSwitcher.UseLocalConfig)
            {
                LoadLocalStrings();
            }
#endif            
            LocaleChanged?.Invoke();
        }
        

        public void DeInit()
        {
        }

        public void NotifyLocaleChanged()
        {
            LocaleChanged?.Invoke();
        }
        
#if UNITY_EDITOR

        private const string LocalizationJsonDir = "Assets/Resources/SayKit/Localizations";

        private void LoadLocalStrings()
        {
            var systemLanguage = GlobalService.SayKit.GetCurrentLanguage();
            var langCode = _languageToCulture.ContainsKey(systemLanguage)
                ? _languageToCulture[systemLanguage]
                : _languageToCulture[AppLanguage.English];

            var jsonPath = $"{LocalizationJsonDir}/saykit_localization_{langCode}.json";
            if (!File.Exists(jsonPath))
            {
                jsonPath = $"{LocalizationJsonDir}/saykit_localization_en.json";
            }

            if (!File.Exists(jsonPath))
            {
                GlobalService.DebugLog.LogError($"Localization JSON not found: {jsonPath}");
                return;
            }

            var json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var obj = JObject.Parse(json);
            _messages = obj["strings"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        }
#endif


    }
}
