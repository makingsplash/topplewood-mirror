using System.Collections;
using System.Collections.Generic;

using Newtonsoft.Json;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsSerializableList<T> : PlayerPrefsValue<List<T>>, IList<T>
    {
        public PlayerPrefsSerializableList(string key, PlayerPrefTags info = default) : base(key, info)
        {
            Refresh();
        }

        protected override void SaveToStorage(List<T> value)
        {
            var json = JsonConvert.SerializeObject(value);
            PlayerPrefsCollection.SetString(Key, json);
        }

        public override void Refresh()
        {
            string json = PlayerPrefsCollection.GetString(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                RealtimeValue = new List<T>();
            }
            else
            {
                RealtimeValue = (List<T>)JsonConvert.DeserializeObject(json, typeof(List<T>));
            }
        }

        public void Add(T item)
        {
            RealtimeValue.Add(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            RealtimeValue.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            var success = RealtimeValue.Remove(item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
            return success;
        }

        public int Count => RealtimeValue.Count;

        public bool IsReadOnly => false;

        public int IndexOf(T item)
        {
            return RealtimeValue.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            RealtimeValue.Insert(index, item);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public void Sort()
        {
            RealtimeValue.Sort();
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public void RemoveAt(int index)
        {
            RealtimeValue.RemoveAt(index);
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public T this[int index]
        {
            get => RealtimeValue[index];
            set
            {
                RealtimeValue[index] = value;
                SaveToStorage(RealtimeValue);
                ChangedInvoke();
            }
        }
        
        public void Clear()
        {
            RealtimeValue.Clear();
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public bool Contains(T item)
        {
            return RealtimeValue.Contains(item);
        }

        public void ElementChanged()
        {
            SaveToStorage(RealtimeValue);
            ChangedInvoke();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return RealtimeValue.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int FindIndex(System.Predicate<T> match)
        {
            return RealtimeValue.FindIndex(match);
        }
    }
}