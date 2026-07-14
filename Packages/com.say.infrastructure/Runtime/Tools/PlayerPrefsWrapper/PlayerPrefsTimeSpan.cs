using System;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsTimeSpan : PlayerPrefsValue<TimeSpan>
    {
        private readonly TimeSpan _defaultValue;
        
        public PlayerPrefsTimeSpan(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }
        
        public PlayerPrefsTimeSpan(string key, TimeSpan defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(TimeSpan value)
        {
            PlayerPrefsCollection.SetString(Key, value.TotalMilliseconds.ToString());
        }

        public override void Refresh()
        {
            string strValue = PlayerPrefsCollection.GetString(Key, null);
            
            if (double.TryParse(strValue, out double binValue) && double.IsNormal(binValue))
            {
                RealtimeValue = TimeSpan.FromMilliseconds(binValue);
            }
            else
            {
                RealtimeValue = _defaultValue;
            }
        }
    }
}