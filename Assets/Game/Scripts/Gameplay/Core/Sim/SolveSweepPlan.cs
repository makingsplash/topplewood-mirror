using System;
using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Builds the release orders to try for a level (RFC §4). Orders are enumerated over the TAPPABLE pieces
    /// only — a preplaced lever/obstacle (TASK-031b) is part of the physics from t=0, is never tap-released, and
    /// so must not permute the release space (video_launcher: 3 tappable → 3! = 6 orders, not 4! = 24 with the
    /// stick as a no-op index). <paramref name="tappableIndices"/> carries the placement indices to order (the
    /// values ReleaseOrderPolicy matches on); for a level with no preplaced piece it is [0..count-1], so the
    /// mapping is identity and the orders are byte-for-byte the pre-TASK-031 set. For a small tappable count it
    /// enumerates EVERY permutation (exhaustive solvability proof); above the threshold it draws a seeded sample
    /// and logs the fallback plus how many orders go uncovered — no silent cap.
    /// TASK-427: the seeded sample alone cannot PROVE a level whose winning set is narrower than ~1/sampleCount of the
    /// permutations (a 6-piece level with one winner out of 720 reads WINNABLE 0/1 although it is solvable). So above
    /// the threshold the level's authored <c>winningOrder</c> is tried as well — see <see cref="PinnedOrder"/>. It is a
    /// SEPARATE bucket (<see cref="PinnedPolicyId"/>), not mixed into the sample: the random sample stays byte-for-byte
    /// what it was, and the loseability audit (arbitrary orders only) never counts the authored winner.
    /// </summary>
    public static class SolveSweepPlan
    {
        /// <summary>PolicyId of the runs that replay the level's authored winning order (TASK-427). The gate
        /// (sim-gate.py: any (policy, order) solved in every repeat) accepts it like any other order; the
        /// loseability audit, which counts only "release-order" runs, ignores it by construction.</summary>
        public const string PinnedPolicyId = "winning-order";

        /// <summary>
        /// The authored winning order to try IN ADDITION to the seeded sample, or null when there is nothing to pin:
        /// the level is enumerated exhaustively (n ≤ <paramref name="maxFullPermute"/> — the order is already among
        /// the permutations), it has no <c>winningOrder</c>, or that order is not a full permutation of the tappable
        /// pieces (a partial / preplaced-referencing order cannot be replayed as a release order; the reason is
        /// logged, never silently dropped). The order is level DATA, not a gate knob: a wrong order simply loses and
        /// proves nothing, so pinning it cannot turn an unsolvable level green.
        /// </summary>
        public static int[] PinnedOrder(
            IReadOnlyList<int> tappableIndices, int maxFullPermute, IReadOnlyList<int> winningOrder, Action<string> log)
        {
            if (tappableIndices.Count <= maxFullPermute || winningOrder == null)
                return null;

            var tappable = new HashSet<int>(tappableIndices);
            var seen = new HashSet<int>();
            foreach (var index in winningOrder)
            {
                if (!tappable.Contains(index) || !seen.Add(index))
                {
                    log?.Invoke($"[level-sim] winningOrder [{string.Join(",", winningOrder)}] is not a permutation of " +
                                $"the {tappableIndices.Count} tappable pieces [{string.Join(",", tappableIndices)}] " +
                                "— NOT pinned; the seeded sample alone decides winnability.");
                    return null;
                }
            }

            if (seen.Count != tappable.Count)
            {
                log?.Invoke($"[level-sim] winningOrder [{string.Join(",", winningOrder)}] covers {seen.Count} of " +
                            $"{tappable.Count} tappable pieces — NOT pinned; the seeded sample alone decides winnability.");
                return null;
            }

            var pinned = new int[winningOrder.Count];
            for (var i = 0; i < pinned.Length; i++)
                pinned[i] = winningOrder[i];

            log?.Invoke($"[level-sim] pinned winningOrder {string.Join("-", pinned)} as policy '{PinnedPolicyId}' " +
                        "(authored order, tried in addition to the seeded sample; not counted by loseability).");
            return pinned;
        }

        public static IReadOnlyList<int[]> BuildOrders(
            IReadOnlyList<int> tappableIndices, int maxFullPermute, int sampleCount, int seed, Action<string> log)
        {
            var n = tappableIndices.Count;
            if (n <= maxFullPermute)
            {
                var perms = Permutations.All(n);
                var all = new List<int[]>(perms.Count);
                foreach (var p in perms)
                    all.Add(MapToPlacementIndices(p, tappableIndices));
                return all;
            }

            var rng = new DeterministicRandom(seed);
            var orders = new List<int[]>(sampleCount);
            var seen = new HashSet<string>();
            for (var i = 0; i < sampleCount; i++)
            {
                var order = MapToPlacementIndices(Permutations.Shuffled(n, rng), tappableIndices);
                if (seen.Add(string.Join("-", order)))
                    orders.Add(order);
            }

            log?.Invoke($"[level-sim] {n} tappable pieces > {maxFullPermute}: sampling {orders.Count} unique " +
                        $"seeded orders of {FactorialText(n)} total permutations (FALLBACK — not exhaustive).");
            return orders;
        }

        /// <summary>Remap a positional permutation of [0..n) onto the actual placement indices of the tappable
        /// pieces, so a preplaced piece's index never appears in a release order.</summary>
        private static int[] MapToPlacementIndices(int[] positional, IReadOnlyList<int> tappableIndices)
        {
            var mapped = new int[positional.Length];
            for (var i = 0; i < positional.Length; i++)
                mapped[i] = tappableIndices[positional[i]];
            return mapped;
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
