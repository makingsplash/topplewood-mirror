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
            SimProvenance provenance, object tapAudit = null, object scope = null, object loseability = null)
        {
            // `generator` is now an object (was the bare string "level-sim v0"): it carries the git provenance
            // of the run (TASK-058) so acceptance reads the ref/dirty from the artifact, not from file mtime.
            // sim-gate.py reads only `runs`, so widening this key does not touch judging.
            var generator = new
            {
                name = GeneratorName,
                commit = provenance.Commit,
                branch = provenance.Branch,
                dirty = provenance.Dirty,
                worktree = provenance.Worktree,
                startedAt = provenance.StartedAt,
            };
            // TASK-179: a targeted (--only) sweep passes a non-null `scope` marker; sim-gate.py reads it and stamps
            // the verdict PARTIAL so it can never be mistaken for the full-lineage gate.
            // TASK-181: a corpus/targeted sweep also passes a non-null `tapAudit` block (per-level tap-spacing audit,
            // sibling of stats/m0/runs); the timing report passes null so its shape is unchanged.
            // TASK-220: the full lineage sweep ALSO passes a non-null `loseability` block (§D16 phase 1 per-level
            // loseability, sibling of tapAudit); the targeted and timing reports pass null. All three optional keys
            // follow the same "no value → no key" discipline: the DEFAULT-shape path (all null → the timing report)
            // stays byte-for-byte identical, and each key appears only when present, in the fixed order
            // scope?, generator, stats, m0, tapAudit?, loseability?, runs. Widening the report with the diagnostic
            // `loseability` key does not touch judging — sim-gate.py reads only `runs` (precedent: TASK-058
            // widened `generator`, TASK-181 added `tapAudit`).
            if (scope == null)
            {
                if (tapAudit == null && loseability == null)
                    return JsonConvert.SerializeObject(
                        new { generator, stats, m0 = m0Summary, runs }, Formatting.Indented);
                if (tapAudit == null)
                    return JsonConvert.SerializeObject(
                        new { generator, stats, m0 = m0Summary, loseability, runs }, Formatting.Indented);
                if (loseability == null)
                    return JsonConvert.SerializeObject(
                        new { generator, stats, m0 = m0Summary, tapAudit, runs }, Formatting.Indented);
                return JsonConvert.SerializeObject(
                    new { generator, stats, m0 = m0Summary, tapAudit, loseability, runs }, Formatting.Indented);
            }
            if (tapAudit == null && loseability == null)
                return JsonConvert.SerializeObject(
                    new { scope, generator, stats, m0 = m0Summary, runs }, Formatting.Indented);
            if (tapAudit == null)
                return JsonConvert.SerializeObject(
                    new { scope, generator, stats, m0 = m0Summary, loseability, runs }, Formatting.Indented);
            if (loseability == null)
                return JsonConvert.SerializeObject(
                    new { scope, generator, stats, m0 = m0Summary, tapAudit, runs }, Formatting.Indented);
            return JsonConvert.SerializeObject(
                new { scope, generator, stats, m0 = m0Summary, tapAudit, loseability, runs }, Formatting.Indented);
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
