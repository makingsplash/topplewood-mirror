using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Rolls raw runs up into per-level solve statistics (RFC §5.4). A release order counts as a RELIABLE
    /// solution only when all its repeats solved AND share one identical pose hash; a level is deterministic
    /// when every order's repeats are hash-stable. Everything is statistics — there is no boolean "solvable".
    /// </summary>
    public static class SimAggregator
    {
        public static IReadOnlyList<LevelSolveStats> Aggregate(IReadOnlyList<SimRunResult> runs)
        {
            var levelOrder = new List<string>();
            var byLevel = new Dictionary<string, List<SimRunResult>>();
            foreach (var r in runs)
            {
                if (!byLevel.TryGetValue(r.LevelId, out var list))
                {
                    list = new List<SimRunResult>();
                    byLevel[r.LevelId] = list;
                    levelOrder.Add(r.LevelId);
                }
                list.Add(r);
            }

            var stats = new List<LevelSolveStats>();
            foreach (var levelId in levelOrder)
                stats.Add(BuildLevelStats(levelId, byLevel[levelId]));
            return stats;
        }

        private static LevelSolveStats BuildLevelStats(string levelId, List<SimRunResult> runs)
        {
            // One bucket per (policy, order): its members are the R repeats of that release order.
            var byOrder = new Dictionary<string, List<SimRunResult>>();
            foreach (var r in runs)
            {
                var key = r.PolicyId + ":" + r.OrderKey;
                if (!byOrder.TryGetValue(key, out var list))
                    byOrder[key] = list = new List<SimRunResult>();
                list.Add(r);
            }

            var reliable = 0;
            var deterministic = true;
            var solveSteps = new List<int>();
            var solvedErrors = new List<float>();

            foreach (var kv in byOrder)
            {
                var reps = kv.Value;
                var hashStable = AllSameHash(reps);
                if (!hashStable)
                    deterministic = false;

                var allSolved = true;
                foreach (var r in reps)
                {
                    if (r.Solved)
                    {
                        solveSteps.Add(r.StepsToResolve);
                        solvedErrors.Add(r.MatchError);
                    }
                    else
                    {
                        allSolved = false;
                    }
                }

                if (allSolved && hashStable)
                    reliable++;
            }

            var ordersTried = byOrder.Count;
            var solveRate = ordersTried == 0 ? 0f : (float)reliable / ordersTried;
            return new LevelSolveStats(levelId, ordersTried, reliable, solveRate, deterministic,
                Avg(solveSteps), Min(solveSteps), Median(solvedErrors));
        }

        private static bool AllSameHash(List<SimRunResult> reps)
        {
            if (reps.Count <= 1)
                return true;

            var first = reps[0].PoseHash;
            for (var i = 1; i < reps.Count; i++)
            {
                if (reps[i].PoseHash != first)
                    return false;
            }
            return true;
        }

        private static float Avg(List<int> values)
        {
            if (values.Count == 0)
                return 0f;

            long sum = 0;
            foreach (var x in values)
                sum += x;
            return (float)sum / values.Count;
        }

        private static int Min(List<int> values)
        {
            if (values.Count == 0)
                return 0;

            var min = int.MaxValue;
            foreach (var x in values)
            {
                if (x < min)
                    min = x;
            }
            return min;
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0)
                return 0f;

            values.Sort();
            var mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5f;
        }
    }
}
