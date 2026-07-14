using System.Collections.Generic;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage
{
    public class DictionaryBackend : IPlayerPrefsBackend
    {
        private readonly Dictionary<string, object> _storage = new();

        public DictionaryBackend()
        {
            
        }

        public DictionaryBackend(Dictionary<string, object> storage)
        {
            _storage = storage;
        }

        public Dictionary<string, object> GetData()
        {
            return _storage;
        }
        
        public int GetInt(string key, int defaultValue)
        {
            if (!_storage.TryGetValue(key, out object value))
            {
                return defaultValue;
            }

            return value switch
            {
                int intValue => intValue,
                long longValue => (int)longValue,
                _ => defaultValue
            };
        }

        public float GetFloat(string key, float defaultValue)
        {
            if (!_storage.ContainsKey(key))
            {
                return defaultValue;
            }
            return _storage[key] is float ? (float)_storage[key] : defaultValue;
        }

        public string GetString(string key, string defaultValue)
        {
            if (!_storage.ContainsKey(key))
            {
                return defaultValue;
            }
            return _storage[key] is string ? (string)_storage[key] : defaultValue;
        }

        public void SetInt(string key, int value)
        {
            _storage[key] = value;
        }

        public void SetFloat(string key, float value)
        {
            _storage[key] = value;
        }

        public void SetString(string key, string value)
        {
            _storage[key] = value;
        }

        public bool HasValue(string key)
        {
            return _storage.ContainsKey(key);
        }
        
        public void DeleteValue(string key)
        {
            _storage.Remove(key);
        }

        public void DeleteAll()
        {
            _storage.Clear();
        }
        
        public void SaveDataIfChanged()
        {
        }
    }
}