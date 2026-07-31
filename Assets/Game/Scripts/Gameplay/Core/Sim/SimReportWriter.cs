using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Builds the CSV + JSON report bodies as strings (RFC §7 п.11) — Unity-free; the host writes them to
    /// disk. CSV rows are assembled by hand (invariant culture); JSON uses Newtonsoft with stable field names.
    /// </summary>
    public static class SimReportWriter
    {
        private const string Header =
            "levelId,seed,policyId,orderKey,repeatIndex,solved,outcome,attemptsUsed,stepsToResolve," +
            "matchError,slotsFilled,slotsTotal,lostPieces,poseHash,preGlideAngResidual,preGlidePosErr," +
            "intersectingPairs,maxPenetration,unsupportedPlaced," +
            "worstPairKindA,worstPairKindB,worstPairPenetration,worstPairBaseline,worstPairSlotA,worstPairSlotB";

        public static string BuildCsv(IReadOnlyList<SimRunResult> runs)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var r in runs)
            {
                sb.Append(r.LevelId).Append(',')
                    .Append(r.Seed).Append(',')
                    .Append(r.PolicyId).Append(',')
                    .Append(r.OrderKey).Append(',')
                    .Append(r.RepeatIndex).Append(',')
                    .Append(r.Solved ? 1 : 0).Append(',')
                    .Append(r.Outcome).Append(',')
                    .Append(r.AttemptsUsed).Append(',')
                    .Append(r.StepsToResolve).Append(',')
                    .Append(F(r.MatchError)).Append(',')
                    .Append(r.SlotsFilled).Append(',')
                    .Append(r.SlotsTotal).Append(',')
                    .Append(r.LostPieces).Append(',')
                    .Append(r.PoseHash).Append(',')
                    .Append(F(r.PreGlideAngResidual)).Append(',')
                    .Append(F(r.PreGlidePosErr)).Append(',')
                    .Append(r.IntersectingPairs).Append(',')
                    .Append(F(r.MaxPenetration)).Append(',')
                    .Append(r.UnsupportedPlaced).Append(',')
                    .Append(r.WorstPair.KindA).Append(',')
                    .Append(r.WorstPair.KindB).Append(',')
                    .Append(F(r.WorstPair.Penetration)).Append(',')
                    .Append(F(r.WorstPair.Baseline)).Append(',')
                    .Append(r.WorstPair.SlotA).Append(',')
                    .Append(r.WorstPair.SlotB).Append('\n');
            }
            return sb.ToString();
        }

        private const string GeneratorName = "level-sim v0";

        public static string BuildJson(
            IReadOnlyList<SimRunResult> runs, IReadOnlyList<LevelSolveStats> stats, object m0Summary,
            SimProvenance provenance)
        {
            // `generator` is now an object (was the bare string "level-sim v0"): it carries the git provenance
            // of the run (TASK-058) so acceptance reads the ref/dirty from the artifact, not from file mtime.
            // sim-gate.py reads only `runs`, so widening this key does not touch judging.
            var payload = new
            {
                generator = new
                {
                    name = GeneratorName,
                    commit = provenance.Commit,
                    branch = provenance.Branch,
                    dirty = provenance.Dirty,
                    worktree = provenance.Worktree,
                    startedAt = provenance.StartedAt,
                },
                stats,
                m0 = m0Summary,
                runs,
            };
            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
