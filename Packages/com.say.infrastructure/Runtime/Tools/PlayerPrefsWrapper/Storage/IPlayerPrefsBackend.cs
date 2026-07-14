using System.Collections.Generic;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage
{
    public interface IPlayerPrefsBackend
    {
       int GetInt(string key, int defaultValue);
       float GetFloat(string key, float defaultValue);
       string GetString(string key, string defaultValue);

       void SetInt(string key, int value);
       void SetFloat(string key, float value);
       void SetString(string key, string value);

        bool HasValue(string key);
        void DeleteValue(string key);
        void DeleteAll();
        void SaveDataIfChanged();
    }
}