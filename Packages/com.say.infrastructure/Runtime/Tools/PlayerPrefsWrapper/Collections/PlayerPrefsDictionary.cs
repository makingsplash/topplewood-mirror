using System.Collections.Generic;
using System.Text;

using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections
{
    public class PlayerPrefsDictionary<TK, TV> : PlayerPrefsValue<Dictionary<TK, TV>>
    {
        private const string _defaultKeyValueSeparator = "#";
        private const string _defaultArraySeparator = ",";

        private readonly string _keyValueSeparator;
        private readonly string _arraySeparator;

        public PlayerPrefsDictionary(string key, string keyValueSeparator = _defaultKeyValueSeparator, string arraySeparator = _defaultArraySeparator, PlayerPrefTags info = default) : base(key, info)
        {
            _keyValueSeparator = keyValueSeparator;
            _arraySeparator = arraySeparator;
            Refresh();
        }

        protected override void SaveToStorage(Dictionary<TK, TV> value)
        {
            StringBuilder builder = new StringBuilder(string.Empty);

            foreach (var pair in value)
            {
                BasePlayerPrefsCollectionData<TK> keyConverter = (BasePlayerPrefsCollectionData<TK>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(TK)];
                keyConverter.Value = pair.Key;
                BasePlayerPrefsCollectionData<TV> valueConverter = (BasePlayerPrefsCollectionData<TV>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(TV)];
                valueConverter.Value = pair.Value;

                builder.Append($"{keyConverter.GetValueString()}{_keyValueSeparator}{valueConverter.GetValueString()}");
                builder.Append(_arraySeparator);
            }
            
            if (value.Count != 0)
            {
                builder.Remove(builder.Length - 1, 1);    
            }
            
            PlayerPrefsCollection.SetString(Key, builder.ToString());
        }

        public sealed override void Refresh()
        {
            RealtimeValue = new Dictionary<TK, TV>();
            string stringRepresentation = PlayerPrefsCollection.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(stringRepresentation))
            {
                string[] valueStrings = stringRepresentation.Split(_arraySeparator);
                for (int i = 0; i < valueStrings.Length; ++i)
                {
                    string valueString = valueStrings[i];
                    if (string.IsNullOrEmpty(valueString))
                    {
                        continue;
                    }
                    string[] kv = valueString.Split(_keyValueSeparator);
                    
                    BasePlayerPrefsCollectionData<TK> keyConverter = (BasePlayerPrefsCollectionData<TK>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(TK)];
                    keyConverter.TryParseValue(kv[0]);

                    BasePlayerPrefsCollectionData<TV> valueConverter = (BasePlayerPrefsCollectionData<TV>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(TV)];
                    valueConverter.TryParseValue(kv[1]);

                    RealtimeValue[keyConverter.Value] = valueConverter.Value;
                }
            }
        }
    }
}