using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Core.Sim;
using Newtonsoft.Json;
using UnityEngine;

namespace Gameplay.Sim
{
    /// <summary>
    /// Unity host of the level-sim harness (RFC §7 п.12): a play-mode MonoBehaviour that drives the real
    /// <see cref="Gameplay.Core.Sim.SimRun"/> over the embedded test levels, aggregates solve statistics,
    /// runs the M0 confirmation experiments (ADR-0001), and writes a CSV + JSON report to <c>Artifacts/</c>.
    /// It injects the production <see cref="UnityShapeFillSimulation"/> (real Physics2D) into the otherwise
    /// container-free domain graph, and honours the determinism mandate (RFC §6): a fresh simulation instance
    /// plus a frame yield between runs so the deferred body <c>Destroy</c> flushes.
    /// </summary>
    public sealed class LevelSimRunner : MonoBehaviour
    {
        [Header("Sweep (config-over-hardcode; ⟨balancer/qa⟩ tune these)")]
        [SerializeField] private int _repeats = 5;                 // R: repeats per release order (RFC §5.3)
        [SerializeField] private int _maxSteps = 3000;             // timeout in fixed steps (~50s game time)
        [SerializeField] private int _sampleCount = 60;            // N: seeded samples when pieces > threshold
        [SerializeField] private int _maxFullPermute = 5;          // ≤ this many pieces → enumerate all orders
        [SerializeField] private bool _runSlotAscendingSmoke = true;

        [Header("M0 confirmation (ADR-0001)")]
        [SerializeField] private int _m0ReproRepeats = 100;        // same-device reproducibility sample size
        [SerializeField] private int[] _accelStepsPerFrame = { 1, 8, 64 };

        [Header("Output")]
        [SerializeField] private string _outputSubdir = "Artifacts/level-sim";
        [SerializeField] private bool _exitOnDone = false;

        // The sim now takes its release-drag knobs from tuning (TASK-009); this standalone harness (outside DI)
        // uses production defaults so its physics matches the runtime.
        private readonly ShapeFillTuning _tuning = new ShapeFillTuning();

        private void Start() => StartCoroutine(RunSweep());

        private IEnumerator RunSweep()
        {
            var levels = new EmbeddedTestLevels().Levels;
            var runs = new List<SimRunResult>();

            foreach (var level in levels)
            {
                var orders = SolveSweepPlan.BuildOrders(
                    level.Pieces.Count, _maxFullPermute, _sampleCount, level.Seed, Debug.Log);

                foreach (var order in orders)
                {
                    var orderKey = string.Join("-", order);
                    for (var r = 0; r < _repeats; r++)
                    {
                        var sim = new UnityShapeFillSimulation(_tuning);
                        runs.Add(SimRun.Run(level, sim, new ReleaseOrderPolicy(order),
                            "release-order", orderKey, r, _maxSteps));
                        yield return null; // RFC §6: let deferred Destroy flush before the next run
                    }
                }

                if (_runSlotAscendingSmoke)
                {
                    for (var r = 0; r < _repeats; r++)
                    {
                        var sim = new UnityShapeFillSimulation(_tuning);
                        runs.Add(SimRun.Run(level, sim, new SlotAscendingPolicy(),
                            "slot-ascending", "slot-asc", r, _maxSteps));
                        yield return null;
                    }
                }
            }

            var stats = SimAggregator.Aggregate(runs);

            object m0 = null;
            yield return StartCoroutine(BuildM0Summary(levels, runs, result => m0 = result));

            WriteReports(runs, stats, m0);
            LogSummary(stats, m0);

            if (_exitOnDone)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        // M0-confirmation experiments (RFC §8): same-device reproducibility, safe acceleration factor,
        // match-tolerance headroom. Cross-device (M0.2 in the ADR) is a manual multi-device run, not here.
        private IEnumerator BuildM0Summary(
            IReadOnlyList<LevelData> levels, IReadOnlyList<SimRunResult> runs, Action<object> done)
        {
            var level = levels[0]; // two_cubes: the deterministic M1 baseline
            var order = Identity(level.Pieces.Count);

            // M0.1 — same-device reproducibility: one fixed order, N fresh-instance repeats, count distinct hashes.
            var hashes = new HashSet<long>();
            for (var i = 0; i < _m0ReproRepeats; i++)
            {
                var sim = new UnityShapeFillSimulation(_tuning);
                var res = SimRun.Run(level, sim, new ReleaseOrderPolicy((int[])order.Clone()),
                    "m0-repro", "identity", i, _maxSteps);
                hashes.Add(res.PoseHash);
                yield return null;
            }
            var reproducible = hashes.Count == 1;

            // M0.2 — safe acceleration: the same order run all-in-one-frame (∞) vs spread across frames in
            // batches of stepsPerFrame must reach an identical final pose hash (RFC §8.2).
            var oneFrameSim = new UnityShapeFillSimulation(_tuning);
            var oneFrameHash = SimRun.Run(level, oneFrameSim, new ReleaseOrderPolicy((int[])order.Clone()),
                "m0-accel", "inf", 0, _maxSteps).PoseHash;
            yield return null;

            var accelSafe = true;
            var accelVariants = new List<object>();
            foreach (var spf in _accelStepsPerFrame)
            {
                var holder = new long[1];
                yield return StartCoroutine(RunChunked(level, (int[])order.Clone(), spf, h => holder[0] = h));
                if (holder[0] != oneFrameHash)
                    accelSafe = false;
                accelVariants.Add(new { stepsPerFrame = spf, poseHash = holder[0] });
            }

            // M0.3 — match-tolerance headroom: distribution of MatchError over solved runs vs the level tolerance.
            var solvedErrors = new List<float>();
            var minAttempts = int.MaxValue;
            foreach (var r in runs)
            {
                if (!r.Solved)
                    continue;
                solvedErrors.Add(r.MatchError);
                if (r.AttemptsUsed < minAttempts)
                    minAttempts = r.AttemptsUsed;
            }
            solvedErrors.Sort();

            done(new
            {
                reproducible,
                distinctHashes = hashes.Count,
                reproRepeats = _m0ReproRepeats,
                accelSafe,
                oneFramePoseHash = oneFrameHash,
                accelVariants,
                matchError = new
                {
                    solvedRuns = solvedErrors.Count,
                    min = solvedErrors.Count > 0 ? solvedErrors[0] : 0f,
                    median = Median(solvedErrors),
                    max = solvedErrors.Count > 0 ? solvedErrors[solvedErrors.Count - 1] : 0f,
                    posTol = level.MatchTolerance.PosTol,
                    angTol = level.MatchTolerance.AngTol,
                },
                minAttemptsToSolve = minAttempts == int.MaxValue ? 0 : minAttempts,
                note = "cross-device reproducibility is a manual multi-device run outside this harness (ADR-0001 M0.2).",
            });
        }

        // Drives a SimRun across multiple frames, yielding every stepsPerFrame steps, so deferred Destroys of
        // any Lost bodies flush mid-run — the sensitive path the accel-safe check probes (RFC §6/§8.2).
        private IEnumerator RunChunked(LevelData level, int[] order, int stepsPerFrame, Action<long> done)
        {
            var sim = new UnityShapeFillSimulation(_tuning);
            using var run = new SimRun(level, sim, new ReleaseOrderPolicy(order));

            var steps = 0;
            var batch = stepsPerFrame < 1 ? 1 : stepsPerFrame;
            while (steps < _maxSteps && !run.Resolved)
            {
                for (var i = 0; i < batch && steps < _maxSteps && !run.Resolved; i++)
                {
                    run.Step();
                    steps++;
                }
                yield return null;
            }

            done(run.PoseHash());
            sim.Reset();
            yield return null;
        }

        private void WriteReports(IReadOnlyList<SimRunResult> runs, IReadOnlyList<LevelSolveStats> stats, object m0)
        {
            var csv = SimReportWriter.BuildCsv(runs);
            var json = SimReportWriter.BuildJson(runs, stats, m0);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var dir = ResolveOutputDir();

            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"level-sim_{stamp}.csv"), csv);
                File.WriteAllText(Path.Combine(dir, $"level-sim_{stamp}.json"), json);
                Debug.Log($"[level-sim] wrote report to {dir} (level-sim_{stamp}.csv / .json)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[level-sim] failed to write report to {dir}: {e.Message}");
            }
        }

        private string ResolveOutputDir()
        {
            // Editor: repo root (parent of /Assets). Player build: persistentDataPath fallback (RFC §7 п.12).
            var repoRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var baseDir = string.IsNullOrEmpty(repoRoot) ? Application.persistentDataPath : repoRoot;
            return Path.Combine(baseDir, _outputSubdir);
        }

        private void LogSummary(IReadOnlyList<LevelSolveStats> stats, object m0)
        {
            foreach (var s in stats)
            {
                Debug.Log($"[level-sim] {s.LevelId}: solveRate={s.SolveRate:0.###} " +
                          $"({s.ReliableSolveOrders}/{s.OrdersTried} reliable orders), deterministic={s.Deterministic}, " +
                          $"avgSteps={s.AvgStepsToSolve:0}, minSteps={s.MinStepsToSolve}, medianMatchError={s.MedianMatchError:0.####}");
            }
            Debug.Log("[level-sim] M0: " + JsonConvert.SerializeObject(m0));
        }

        private static int[] Identity(int n)
        {
            var arr = new int[n];
            for (var i = 0; i < n; i++)
                arr[i] = i;
            return arr;
        }

        private static float Median(List<float> sortedValues)
        {
            if (sortedValues.Count == 0)
                return 0f;
            var mid = sortedValues.Count / 2;
            return sortedValues.Count % 2 == 1
                ? sortedValues[mid]
                : (sortedValues[mid - 1] + sortedValues[mid]) * 0.5f;
        }
    }
}
