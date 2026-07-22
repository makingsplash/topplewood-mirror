using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Deterministic permutation helpers (RFC §4/§7 п.7). <see cref="All"/> enumerates every permutation of
    /// 0..n-1 in a fixed order (used for ≤5 pieces, ≤120 orders); <see cref="Shuffled"/> draws one seeded
    /// permutation via Fisher–Yates for the many-piece fallback.
    /// </summary>
    public static class Permutations
    {
        public static IReadOnlyList<int[]> All(int n)
        {
            var identity = new int[n];
            for (var i = 0; i < n; i++)
                identity[i] = i;

            var result = new List<int[]>();
            if (n > 0)
                Recurse(identity, 0, result);
            return result;
        }

        private static void Recurse(int[] arr, int k, List<int[]> result)
        {
            if (k == arr.Length - 1)
            {
                result.Add((int[])arr.Clone());
                return;
            }

            for (var i = k; i < arr.Length; i++)
            {
                (arr[k], arr[i]) = (arr[i], arr[k]);
                Recurse(arr, k + 1, result);
                (arr[k], arr[i]) = (arr[i], arr[k]);
            }
        }

        public static int[] Shuffled(int n, IGameplayRandom rng)
        {
            var arr = new int[n];
            for (var i = 0; i < n; i++)
                arr[i] = i;

            for (var i = n - 1; i > 0; i--)
            {
                var j = rng.NextInt(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }
    }
}
