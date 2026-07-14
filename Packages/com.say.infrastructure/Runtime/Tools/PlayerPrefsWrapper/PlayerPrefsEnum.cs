using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public class PlayerPrefsEnum<T> : PlayerPrefsValue<T> where T : Enum 
    {
        private readonly T _defaultValue;
        
        public PlayerPrefsEnum(string key, T defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(T value)
        {
            PlayerPrefsCollection.SetString(Key, value.ToString());
        }

        public override void Refresh()
        {
            var value = PlayerPrefsCollection.GetString(Key, _defaultValue.ToString());
            try
            {
                RealtimeValue = (T)Enum.Parse(typeof(T), value);
            }
            catch (ArgumentException)
            {
                RealtimeValue = _defaultValue;
            }
        }
    }
}