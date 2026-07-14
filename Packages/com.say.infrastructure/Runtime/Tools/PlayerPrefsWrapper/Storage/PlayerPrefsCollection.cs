using System;
using System.Collections.Generic;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage
{
    public class PlayerPrefsCollection
    {
        private static PlayerPrefsCollection _instance;
        private readonly List<WeakReference<PlayerPrefsBase>> _allPrefs = new();
        private IPlayerPrefsBackend _backend;

        public static void Init(IPlayerPrefsBackend backend)
        {
            _instance = new PlayerPrefsCollection(backend);
        }

        public static PlayerPrefsCollection Instance => _instance;
        public IPlayerPrefsBackend Backend => _backend;

        private PlayerPrefsCollection(IPlayerPrefsBackend backend)
        {
            _backend = backend;
        }

        public void SaveDataIfChanged()
        {
            _backend.SaveDataIfChanged();
        }

        public void RefreshAll()
        {
            bool anyDeadReference = false;
            foreach (WeakReference<PlayerPrefsBase> playerPrefsReference in _allPrefs)
            {
                if (playerPrefsReference.TryGetTarget(out PlayerPrefsBase playerPrefs))
                {
                    playerPrefs.Refresh();
                }
                else
                {
                    anyDeadReference = true;
                }
            }

            if (anyDeadReference)
            {
                _allPrefs.RemoveAll(playerPrefsReference => playerPrefsReference.TryGetTarget(out _) == false);
            }
        }
        
        public void DeletePrefValue(PlayerPrefsBase pref)
        {
            _backend.DeleteValue(pref.Key);
        }

        public void DeleteAllPrefs()
        {
            _backend.DeleteAll();
        }

        public void SaveToBackend(IPlayerPrefsBackend backend, Func<PlayerPrefTags, bool> filter)
        {
            var curBackend = _backend;
            _backend = backend;

            SaveAllPrefs(filter);

            _backend = curBackend;
        }

        public void LoadFromBackend(IPlayerPrefsBackend backend, Func<PlayerPrefTags, bool> filter)
        {
            var curBackend = _backend;
            _backend = backend;
            foreach (WeakReference<PlayerPrefsBase> playerPrefsReference in _allPrefs)
            {
                if (!playerPrefsReference.TryGetTarget(out PlayerPrefsBase pref))
                {
                    continue;
                }

                if (filter != null && !filter(pref.Info))
                {
                    continue;
                }
                pref.Refresh();
            }
            _backend = curBackend;
            
            // save changes to the main storage 
            SaveAllPrefs(filter);
        }

        private void SaveAllPrefs(Func<PlayerPrefTags, bool> filter)
        {
            foreach (WeakReference<PlayerPrefsBase> playerPrefsReference in _allPrefs)
            {
                if (!playerPrefsReference.TryGetTarget(out PlayerPrefsBase pref))
                {
                    continue;
                }

                if (filter != null && !filter(pref.Info))
                {
                    continue;
                }
                pref.ForceSave();
            }
        }

#region PlayerPref internal interface 

        internal static void RegisterPlayerPref(PlayerPrefsBase playerPrefs)
        {
            _instance._allPrefs.Add(new WeakReference<PlayerPrefsBase>(playerPrefs));
        }
        internal static void UnregisterPlayerPref(PlayerPrefsBase toRemove)
        {
            _instance._allPrefs.RemoveAll(playerPrefsReference => playerPrefsReference.TryGetTarget(out PlayerPrefsBase playerPrefs) == false || playerPrefs == toRemove);
        }

        internal static int GetInt(string key, int defaultValue)
        {
            return _instance._backend.GetInt(key, defaultValue);
        }

        internal static float GetFloat(string key, float defaultValue)
        {
            return _instance._backend.GetFloat(key, defaultValue);
        }
        
        internal static string GetString(string key, string defaultValue)
        {
            return _instance._backend.GetString(key, defaultValue);
        }


        internal static void SetInt(string key, int value)
        {
            _instance._backend.SetInt(key, value);
        }

        internal static void SetFloat(string key, float value)
        {
            _instance._backend.SetFloat(key, value);
        }

        internal static void SetString(string key, string value)
        {
            _instance._backend.SetString(key, value);
        }
#endregion
    }
}