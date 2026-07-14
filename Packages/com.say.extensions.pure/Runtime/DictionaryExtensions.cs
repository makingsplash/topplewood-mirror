using System.Collections.Generic;

namespace SGS.Extensions
{
    public static class DictionaryExtensions
    {
        public static void RemoveAt<TKey,TValue>(this Dictionary<TKey, TValue> dict, TKey where, TValue expectedValue) where TValue : class
        {
            if (!dict.ContainsKey(where))
            {
                return;
            }

            if (dict[where] != expectedValue)
            {
                return;
            }

            dict.Remove(where);
        }
    }
}