using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public struct PlayerPrefTags
    {
        private readonly string[] _tags;

        public string[] Tags => _tags ?? Array.Empty<string>();
        
        public PlayerPrefTags(params string[] tags)
        {
            _tags = tags;
        }
    }

    [Serializable]
    public abstract class PlayerPrefsBase
    {
        public readonly string Key;
        public readonly PlayerPrefTags Info;
        
        protected PlayerPrefsBase(string key, PlayerPrefTags info)
        {
            Info = info;
            Key = key;
            
            PlayerPrefsCollection.RegisterPlayerPref(this);
        }

        ~PlayerPrefsBase()
        {
            PlayerPrefsCollection.UnregisterPlayerPref(this);
        }
        
        public abstract void Refresh();
        public abstract void ForceSave();
    }
}
