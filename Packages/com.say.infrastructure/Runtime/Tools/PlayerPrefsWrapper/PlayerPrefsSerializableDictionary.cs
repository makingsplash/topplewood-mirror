using System.Collections;
using System.Collections.Generic;

using Newtonsoft.Json;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsSerializableDictionary<T> : PlayerPrefsValue<Dictionary<string,T>>, IDictionary<string,T>
    {
        public PlayerPrefsSerializableDictionary(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }

        protected override void SaveToStorage(Dictionary<string,T> value)
        {
            var json = JsonConvert.SerializeObject(value);
            PlayerPrefsCollection.SetString(Key, json);
        }

        public override void Refresh()
        {
            string json = PlayerPrefsCollection.GetString(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                RealtimeValue = new Dictionary<string,T> ();
            }
            else
            {
                RealtimeValue = (Dictionary<string,T>)JsonConvert.DeserializeObject(json, typeof(Dictionary<string,T> ));
            }
        }

        public void ElementChanged()
        {
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
        {
            return RealtimeValue.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)RealtimeValue).GetEnumerator();
        }

        public void Add(KeyValuePair<string, T> item)
        {
            ((IDictionary<string,T>)RealtimeValue).Add(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public void Clear()
        {
            RealtimeValue.Clear();
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public bool Contains(KeyValuePair<string, T> item)
        {
            return ((IDictionary<string,T>)RealtimeValue).Contains(item);
        }

        public void CopyTo(KeyValuePair<string, T>[] array, int arrayIndex)
        {
            ((IDictionary<string,T>)RealtimeValue).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<string, T> item)
        {
            var res = ((IDictionary<string,T>)RealtimeValue).Remove(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
            return res;
        }

        public int Count => RealtimeValue.Count;

        public bool IsReadOnly => false;

        public void Add(string key, T value)
        {
            RealtimeValue.Add(key, value);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public bool ContainsKey(string key)
        {
            return RealtimeValue.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            var res = RealtimeValue.Remove(key);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
            return res;
        }

        public bool TryGetValue(string key, out T value)
        {
            return RealtimeValue.TryGetValue(key, out value);
        }
        
        public T GetValue(string key, T defaultValue)
        {
            if (RealtimeValue.TryGetValue(key, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public T this[string key]
        {
            get => RealtimeValue[key];
            set
            {
                RealtimeValue[key] = value;
                SaveToStorage(RealtimeValue);
                ChangedInvoke();
            }
        }

        public ICollection<string> Keys => RealtimeValue.Keys;

        public ICollection<T> Values => RealtimeValue.Values;
    }
}