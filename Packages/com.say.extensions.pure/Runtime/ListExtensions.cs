using System;
using System.Collections.Generic;
using System.Linq;

namespace SGS.Extensions
{
    public static class ListExtensions
    {
        
        public static T RandomElement<T>(this IReadOnlyList<T> list, System.Random random)
        {
            if (list == null) return default;
            if (list.Count == 0) return default;
            
            var cellsCount = list.Count;
            var i = random.Next(cellsCount);
            return list[i];
        }

        
        public static T RandomElementByWeight<T>(this IReadOnlyList<T> list, System.Random random, Func<T, float> weightFunc) 
        {
            if (list == null) return default;
            if (list.Count == 0) return default;
            

            int result = 0;
            float sum = 0;

            foreach (var element in list)
            {
                sum += weightFunc(element);
            }

            var randPos = (float)random.NextDouble() *  sum;

            float sum2 = 0;
            for (int i = 0; i < list.Count; i++)
            {
                float weight = weightFunc(list[i]);
                if (!Approximately(weight, 0))
                {
                    if (sum2 <= randPos)
                    {
                        result = i;
                    }
                    else
                    {
                        break;
                    }
                }

                sum2 += weight;
            }

            bool Approximately(float a, float b)
            {
                return Math.Abs(a - b) < 1e-6f;
            }

            return list[result];
        }

        public static int Sum(IEnumerable<int> collection)
        {
            var result = 0;
            foreach (var i in collection)
            {
                result += i;
            }

            return result;
        }
        
        public static string ToValuesString(this IEnumerable<int> collection, string separator)
        {
            var result = "";
            
            if (collection != null)
            {
                var enumerator = collection.GetEnumerator();
                var nextExists = enumerator.MoveNext();
                while (nextExists)
                {
                    var cur = enumerator.Current;
                    result += cur.ToString();
                    
                    nextExists = enumerator.MoveNext();
                    if (nextExists)
                    {
                        result += separator;
                    }
                }
                enumerator.Dispose();
            }

            return result;
        }
        
        public static void ShiftLeft<T>(this List<T> lst, int shifts)
        {
            for (int i = shifts; i < lst.Count; i++)
            {
                lst[i - shifts] = lst[i];
            }

            for (int i = lst.Count - shifts; i < lst.Count; i++)
            {
                lst[i] = default(T);
            }
        }
        
        public static void ShiftRight<T>(this List<T> lst, int shifts)
        {
            for (int i = lst.Count - shifts - 1; i >= 0; i--)
            {
                lst[i + shifts] = lst[i];
            }

            for (int i = 0; i < shifts; i++)
            {
                lst[i] = default(T);
            }
        }
        
        public static void ShiftLeft<T>(this T[] arr, int shifts)
        {
            Array.Copy(arr, shifts, arr, 0, arr.Length - shifts);
            Array.Clear(arr, arr.Length - shifts, shifts);
        }

        public static void ShiftRight<T>(this T[] arr, int shifts)
        {
            Array.Copy(arr, 0, arr, shifts, arr.Length - shifts);
            Array.Clear(arr, 0, shifts);
        }

        public static void Shuffle<T>(this T[] list, System.Random random)
        {
            // Using Fisher–Yates "inside-out" shuffle algorithm
            for (int index = 0; index < list.Length; index++)
            {
                T level = list[index];
                var newIndex = random.Next(index + 1);
                if (newIndex != index)
                {
                    list[index] = list[newIndex];
                }

                list[newIndex] = level;
            }
        }
        
        public static void Shuffle<T>(this T[] list, int startIdx, System.Random random)
        {
            for (int index = startIdx; index < list.Length; index++)
            {
                T level = list[index];
                var newIndex = random.Next(startIdx, index + 1);
                if (newIndex != index)
                {
                    list[index] = list[newIndex];
                }

                list[newIndex] = level;
            }
        }

        public static TSource? BestElement<TSource>(this IEnumerable<TSource> source, Func<TSource, TSource, bool> betterFunc) where TSource : struct
        {
            TSource? best = null;
            foreach (var element in source)
            {
                if (!best.HasValue)
                {
                    best = element;
                }
                else if( betterFunc(element, best.Value))
                {
                    best = element;
                }
            }

            return best;
        }
        
        public static IEnumerable<Tuple<T, T>> AsPairs<T>(this IList<T> list) 
        {
            for (int i = 0; i < list.Count - 1; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    yield return new Tuple<T, T>(list[i], list[j]);
                }
            }
        }
        
        public static TSource MaxElement<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector)
        {
            TSource best = default(TSource);
            int bestValue = 0;
            bool hasBest = false;
            foreach (var element in source)
            {
                if (!hasBest)
                {
                    bestValue = selector(element);
                    best = element;
                    hasBest = true;
                }
                else
                {
                    var value = selector(element);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = element;
                    }
                }
            }
            return best;
        }
        
        public static TSource MaxElement<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector)
        {
            TSource best = default(TSource);
            float bestValue = 0;
            bool hasBest = false;
            foreach (var element in source)
            {
                if (!hasBest)
                {
                    bestValue = selector(element);
                    best = element;
                    hasBest = true;
                }
                else
                {
                    var value = selector(element);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = element;
                    }
                }
            }
            return best;
        }

        public static IReadOnlyList<TSource> MaxElements<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector)
        {
            List<TSource> best = new();
            int bestValue = 0;
            
            foreach (var element in source)
            {
                var value = selector(element);
                if (best.Count == 0)
                {
                    bestValue = value;
                    best.Add(element);
                    continue;
                }

                if (value < bestValue)
                {
                    continue;
                }

                if (value > bestValue)
                {
                    best.Clear();
                    bestValue = value;
                }

                best.Add(element);
            }

            return best;
        }

        public static IReadOnlyList<TSource> MaxElements<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector, float epsilon = 1e-6f)
        {
            List<TSource> best = new();
            float bestValue = 0;
            
            foreach (var element in source)
            {
                var value = selector(element);
                if (best.Count == 0)
                {
                    bestValue = value;
                    best.Add(element);
                    continue;
                }

                if (value < bestValue - epsilon)
                {
                    continue;
                }

                if (value > bestValue + epsilon)
                {
                    best.Clear();
                    bestValue = value;
                }

                best.Add(element);
            }

            return best;
        }


        public static List<int> RandomIndices(System.Random random, int count, int from, int to)
        {
            var indices = Enumerable.Range(from, to - from).ToArray();

            var result = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int idx = random.Next(indices.Length - i);
                result.Add(indices[idx]);

                //replace used element with last
                indices[idx] = indices[indices.Length - i - 1];
            }

            return result;
        }
        
        public static List<int> RandomSet(System.Random random, int count, int[] elements)
        {
            var indices = elements.ToArray();

            var result = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int idx = random.Next(indices.Length - i);
                result.Add(indices[idx]);

                //replace used element with last
                indices[idx] = indices[indices.Length - i - 1];
            }

            return result;
        }
    }

}