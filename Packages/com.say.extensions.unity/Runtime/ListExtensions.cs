using System.Collections.Generic;
using UnityEngine;

namespace SGS.UnityExtensions
{
    public static class ListExtensions
    {
        public static T RandomElement<T>(this IReadOnlyList<T> list)
        {
            if (list == null) return default;
            if (list.Count == 0) return default;

            var cellsCount = list.Count;

            var i = Random.Range(0, cellsCount);
            return list[i];
        }
        
        public static void Shuffle<T>(this List<T> list)
        {
            // Using Fisher–Yates "inside-out" shuffle algorithm
            for (int index = 0; index < list.Count; index++)
            {
                T level = list[index];
                var newIndex = Random.Range(0, index + 1);
                if (newIndex != index)
                {
                    list[index] = list[newIndex];
                }

                list[newIndex] = level;
            }
        }
    }
}