using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsDateTime : PlayerPrefsValue<DateTime>
    {
        private readonly DateTime _defaultValue;
        
        public PlayerPrefsDateTime(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }
        
        public PlayerPrefsDateTime(string key, DateTime defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(DateTime value)
        {
            PlayerPrefsCollection.SetString(Key, value.ToBinary().ToString());
        }

        public override void Refresh()
        {
            string strValue = PlayerPrefsCollection.GetString(Key, null);
            
            if (long.TryParse(strValue, out long binValue))
            {
                RealtimeValue = DateTime.FromBinary(binValue);
            }
            else
            {
                RealtimeValue = _defaultValue;
            }
        }
    }
}