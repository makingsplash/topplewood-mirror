using System;
using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Builds the release orders to try for a level (RFC §4). For a small piece count it enumerates EVERY
    /// permutation (exhaustive solvability proof); above the threshold it draws a seeded sample and logs the
    /// fallback plus how many orders go uncovered — no silent cap.
    /// </summary>
    public static class SolveSweepPlan
    {
        public static IReadOnlyList<int[]> BuildOrders(
            int pieceCount, int maxFullPermute, int sampleCount, int seed, Action<string> log)
        {
            if (pieceCount <= maxFullPermute)
                return Permutations.All(pieceCount);

            var rng = new DeterministicRandom(seed);
            var orders = new List<int[]>(sampleCount);
            var seen = new HashSet<string>();
            for (var i = 0; i < sampleCount; i++)
            {
                var order = Permutations.Shuffled(pieceCount, rng);
                if (seen.Add(string.Join("-", order)))
                    orders.Add(order);
            }

            log?.Invoke($"[level-sim] {pieceCount} pieces > {maxFullPermute}: sampling {orders.Count} unique " +
                        $"seeded orders of {FactorialText(pieceCount)} total permutations (FALLBACK — not exhaustive).");
            return orders;
        }

        private static string FactorialText(int n)
        {
            if (n > 20)
                return "very many";

            long f = 1;
            for (var i = 2; i <= n; i++)
                f *= i;
            return f.ToString();
        }
    }
}
