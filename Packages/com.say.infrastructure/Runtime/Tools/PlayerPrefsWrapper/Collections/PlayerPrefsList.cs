using System.Collections.Generic;
using System.Text;

using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections
{
    public class PlayerPrefsList<T> : PlayerPrefsValue<List<T>>
    {
        private const string _arraySeparator = ",";

        public PlayerPrefsList(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }

        public sealed override void Refresh()
        {
            RealtimeValue = new List<T>();
            string stringRepresentation = PlayerPrefsCollection.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(stringRepresentation))
            {
                string[] stringValues = stringRepresentation.Split(_arraySeparator);
                BasePlayerPrefsCollectionData<T> converter = (BasePlayerPrefsCollectionData<T>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(T)];
                foreach (string str in stringValues)
                {
                    converter.TryParseValue(str);
                    Value.Add(converter.Value);
                }
            }
        }
        
        protected override void SaveToStorage(List<T> value)
        {
            StringBuilder builder = new StringBuilder();
            BasePlayerPrefsCollectionData<T> dataConverter = (BasePlayerPrefsCollectionData<T>)PlayerPrefsCollectionDatas.TypeToCollectionData[typeof(T)];
            for (int i = 0; i < value.Count; ++i)
            {
                dataConverter.Value = value[i];
                builder.Append(dataConverter.GetValueString());
                if (i != value.Count - 1)
                {
                    builder.Append(_arraySeparator);
                }
            }

            PlayerPrefsCollection.SetString(Key, builder.ToString());
        }
    }
}