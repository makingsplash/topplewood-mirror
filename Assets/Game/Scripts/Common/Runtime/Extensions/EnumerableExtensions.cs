using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.Pool;

namespace Common.Extensions
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> ToEnumerable<T>(this T value)
        {
            yield return value;
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<Tuple<TKey, TValue>> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable.ToDictionary(tuple => tuple.Item1, tuple => tuple.Item2);
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<ValueTuple<TKey, TValue>> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable.ToDictionary(tuple => tuple.Item1, tuple => tuple.Item2);
        }

        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> valueFactory)
        {
            if (dictionary is null)
                throw new ArgumentNullException(nameof(dictionary));

            if (valueFactory is null)
                throw new ArgumentNullException(nameof(valueFactory));

            if (key is null)
                throw new ArgumentNullException(nameof(key));

            if (dictionary.TryGetValue(key, out var value))
                return value;

            value = valueFactory();
            dictionary.Add(key, value);
            return value;
        }

        // From .net 6 source
        public static IEnumerable<IEnumerable<TSource>> Chunk<TSource>(this IEnumerable<TSource> source, int size)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            if (size < 1)
                throw new ArgumentOutOfRangeException(nameof(size));

            using var e = source.GetEnumerator();
            while (e.MoveNext())
            {
                var chunk = new TSource[size];
                chunk[0] = e.Current;

                var i = 1;
                for (; i < chunk.Length && e.MoveNext(); i++)
                    chunk[i] = e.Current;

                if (i == chunk.Length)
                {
                    yield return chunk;
                    continue;
                }

                Array.Resize(ref chunk, i);
                yield return chunk;
                yield break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsList<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable is List<T>;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static List<T> SafeToList<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable as List<T> ?? enumerable.ToList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IReadOnlyList<T> SafeToReadOnlyList<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            return enumerable as IReadOnlyList<T> ?? enumerable.ToList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledList<T>(this IEnumerable<T> enumerable, out List<T> list)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            var disposable = ListPool<T>.Get(out list);
            list.AddRange(enumerable);

            return disposable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledHashSet<T>(this IEnumerable<T> enumerable, out HashSet<T> set)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            var disposable = HashSetPool<T>.Get(out set);
            set.AddRange(enumerable);

            return disposable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledDictionary<TSource, TKey, TElement>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            out Dictionary<TKey, TElement> dictionary)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            if (keySelector is null)
                throw new ArgumentNullException(nameof(keySelector));

            if (elementSelector is null)
                throw new ArgumentNullException(nameof(elementSelector));

            var disposable = DictionaryPool<TKey, TElement>.Get(out dictionary);
            foreach (var item in source)
                dictionary.Add(keySelector(item), elementSelector(item));

            return disposable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> enumerable,
            out Dictionary<TKey, TValue> dictionary)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            var disposable = DictionaryPool<TKey, TValue>.Get(out dictionary);
            foreach (var (key, value) in enumerable)
                dictionary[key] = value;

            return disposable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledDictionary<TKey, TValue>(this IEnumerable<Tuple<TKey, TValue>> enumerable,
            out Dictionary<TKey, TValue> dictionary)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            var disposable = DictionaryPool<TKey, TValue>.Get(out dictionary);
            foreach (var (key, value) in enumerable)
                dictionary[key] = value;

            return disposable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IDisposable ToPooledDictionary<TKey, TValue>(this IEnumerable<ValueTuple<TKey, TValue>> enumerable,
            out Dictionary<TKey, TValue> dictionary)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            var disposable = DictionaryPool<TKey, TValue>.Get(out dictionary);
            foreach (var (key, value) in enumerable)
                dictionary[key] = value;

            return disposable;
        }

        //<summary>Finds the index of the first occurrence of an item in an enumerable.</summary>
        ///<param name="enumerable">The enumerable to search.</param>
        ///<param name="item">The item to find.</param>
        ///<returns>The index of the first matching item, or -1 if the item was not found.</returns>
        public static int IndexOf<TItem>(this IEnumerable<TItem> enumerable, TItem item)
        {
            var comparer = EqualityComparer<TItem>.Default;
            return enumerable.IndexOf(i => comparer.Equals(item, i));
        }

        ///<summary>Finds the index of the first item matching an expression in an enumerable.</summary>
        ///<param name="enumerable">The enumerable to search.</param>
        ///<param name="predicate">The expression to test the items against.</param>
        ///<returns>The index of the first matching item, or -1 if no items match.</returns>
        public static int IndexOf<TItem>(this IEnumerable<TItem> enumerable, Predicate<TItem> predicate)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            var index = 0;
            foreach (var item in enumerable)
            {
                if (predicate(item))
                    return index;

                index++;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));

            if (action is null)
                throw new ArgumentNullException(nameof(action));

            foreach (var item in enumerable)
                action(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> range)
        {
            if (hashSet is null)
                throw new ArgumentNullException(nameof(hashSet));

            foreach (var item in range)
                hashSet.Add(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddRange<T>(this Queue<T> queue, IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
                queue.Enqueue(item);
        }

        public static bool IsNullOrEmpty<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable == null)
                return true;

            if (enumerable is ICollection<T> collection)
                return collection.Count == 0;

            return !enumerable.Any();
        }

        public static T PickRandom<T>(this IEnumerable<T> collection)
        {
            var enumerable = collection as T[] ?? collection.ToArray();
            return enumerable[UnityEngine.Random.Range(0, enumerable.Length)];
        }

        public static IEnumerable<T> Except<T>(this IEnumerable<T> enumerable, T toExcept) =>
            enumerable.Except(new[] { toExcept });

        public static T ElementAtOrFirst<T>(this T[] array, int index) => index < array.Length ? array[index] : array[0];

        public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T> self) => self ?? Enumerable.Empty<T>();

        public static IEnumerable<T> NoNulls<T>(this IEnumerable<T> self) => self.Where(element => element != null);
    }
}
