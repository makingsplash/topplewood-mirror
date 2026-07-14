using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage
{
    public class PrefsStorageBackend : IPlayerPrefsBackend
    {
        private readonly HashSet<string> _knownKeys = new();
        private bool _haveChanges;

        public int GetInt(string key, int defaultValue)
        {
            _knownKeys.Add(key);
            return PlayerPrefs.GetInt(key, defaultValue);
        }

        public float GetFloat(string key, float defaultValue)
        {
            _knownKeys.Add(key);
            return PlayerPrefs.GetFloat(key, defaultValue);
        }

        public string GetString(string key, string defaultValue)
        {
            _knownKeys.Add(key);
            return PlayerPrefs.GetString(key, defaultValue);
        }

        public void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            _haveChanges = true;
        }

        public void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            _haveChanges = true;
        }

        public void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            _haveChanges = true;
        }

        public bool HasValue(string key)
        {
            return PlayerPrefs.HasKey(key);
        }

        public void DeleteValue(string key)
        {
            PlayerPrefs.DeleteKey(key);
            _haveChanges = true;
        }

        public void DeleteAll()
        {
            foreach (var key in _knownKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        public void SaveDataIfChanged()
        {
            if (!_haveChanges)
            {
                return;
            }

            _haveChanges = false;
            PlayerPrefs.Save();
        }
    }
}