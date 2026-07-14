using System.Collections.Generic;
using System.Text;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsListString : PlayerPrefsValue<List<string>>
    {
        private readonly string _defaultValue;
        
        public PlayerPrefsListString(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }
        
        public PlayerPrefsListString(string key, List<string> defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < defaultValue.Count; ++i)
            {
                builder.Append(defaultValue[i]);
                if (i != defaultValue.Count - 1)
                {
                    builder.Append(",");
                }
            }
            _defaultValue = builder.ToString();
            Refresh();
        }

        protected override void SaveToStorage(List<string> value)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Count; ++i)
            {
                builder.Append(value[i]);
                if (i != value.Count - 1)
                {
                    builder.Append(",");
                }
            }
            PlayerPrefsCollection.SetString(Key, builder.ToString());
        }

        public override void Refresh()
        {
            string saveStr = PlayerPrefsCollection.GetString(Key, _defaultValue);
            
            if (string.IsNullOrWhiteSpace(saveStr))
            {
                RealtimeValue = new List<string>();
            }
            else
            {
                RealtimeValue = new List<string>(saveStr.Split(","));
            }
        }

        public void Add(string item)
        {
            RealtimeValue.Add(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }
        
        public void Remove(string item)
        {
            RealtimeValue.Remove(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }
        
        public void Clear()
        {
            RealtimeValue.Clear();
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }
    }
}