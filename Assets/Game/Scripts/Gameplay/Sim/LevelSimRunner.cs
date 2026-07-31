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
    /// <see cref="Gameplay.Core.Sim.SimRun"/> over the shipped campaign levels, aggregates solve statistics,
    /// runs the M0 confirmation experiments (ADR-0001), and writes a CSV + JSON report to <c>Artifacts/</c>.
    /// It injects the production <see cref="UnityShapeFillSimulation"/> (real Physics2D) into the otherwise
    /// container-free domain graph, and honours the determinism mandate (RFC §6): a fresh simulation instance
    /// plus a frame yield between runs so the deferred body <c>Destroy</c> flushes.
    /// </summary>
    /// <remarks>
    /// Canonical execution is headless <c>-batchmode -executeMethod Gameplay.Sim.Editor.LevelSimBatch.Run</c>
    /// (RFC-Δ / ADR-0001 Block 4/5): batchmode spins the player loop regardless of window focus, so the
    /// multi-frame coroutine completes and the report reaches disk. An interactive / unfocused (MCP-driven)
    /// editor throttles frames and may not finish it — see <c>.claude/skills/level-sim/SKILL.md</c>.
    /// </remarks>
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

        [Header("Output / batch execution")]
        [SerializeField] private string _outputSubdir = "Artifacts/level-sim";
        [SerializeField] private bool _exitOnDone = false;
        [SerializeField] private float _batchTimeoutSeconds = 300f; // watchdog: abort a wedged CI run (RFC-Δ §4.2)

        // The sim now takes its release-drag knobs from tuning (TASK-009); this standalone harness (outside DI)
        // uses production defaults so its physics matches the runtime.
        private readonly ShapeFillTuning _tuning = new ShapeFillTuning();

        private float _startRealtime;
        private DateTime _startWallClock;   // wall-clock sweep start → report provenance `startedAt` (TASK-058)
        private bool _m0Faulted;

        private void Awake()
        {
            // Best-effort (RFC-Δ §7): if an interactive/MCP play-mode entered, keep the loop ticking without
            // focus so the coroutine can still finish and write the report.
            Application.runInBackground = true;

            // Headless batch MUST self-exit (do not trust the scene-serialized flag).
            if (Application.isBatchMode)
                _exitOnDone = true;
        }

        private void Start() => StartCoroutine(RunSweep());

        private IEnumerator RunSweep()
        {
            _startRealtime = Time.realtimeSinceStartup;
            _startWallClock = DateTime.Now;
            _m0Faulted = false;
            var ok = true;
            var runs = new List<SimRunResult>();
            IReadOnlyList<LevelData> levels = null;

            // GAP-1 (human 2026-07-23): sweep the ACTUAL shipped campaign, not embedded stubs.
            try
            {
                levels = LoadLevels();
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] failed to load campaign '{JsonLevelSource.CampaignFileName}': {e}");
                ok = false;
            }

            if (ok)
            {
                foreach (var level in levels)
                {
                    IReadOnlyList<int[]> orders = null;
                    try
                    {
                        orders = SolveSweepPlan.BuildOrders(
                            TappableIndices(level), _maxFullPermute, _sampleCount, level.Seed, Debug.Log);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[level-sim] {level.LevelId}: failed to build release orders: {e}");
                        ok = false;
                    }

                    if (!ok)
                        break;

                    foreach (var order in orders)
                    {
                        var orderKey = string.Join("-", order);
                        for (var r = 0; r < _repeats; r++)
                        {
                            var sim = new UnityShapeFillSimulation(_tuning);
                            try
                            {
                                runs.Add(SimRun.Run(level, sim, new ReleaseOrderPolicy(order),
                                    "release-order", orderKey, r, _maxSteps));
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[level-sim] {level.LevelId} order {orderKey} run {r} threw: {e}");
                                ok = false;
                            }

                            yield return null; // RFC §6: let deferred Destroy flush before the next run

                            if (!ok || WatchdogExpired())
                            {
                                ok = false;
                                break;
                            }
                        }

                        if (!ok)
                            break;
                    }

                    if (ok && _runSlotAscendingSmoke)
                    {
                        for (var r = 0; r < _repeats; r++)
                        {
                            var sim = new UnityShapeFillSimulation(_tuning);
                            try
                            {
                                runs.Add(SimRun.Run(level, sim, new SlotAscendingPolicy(),
                                    "slot-ascending", "slot-asc", r, _maxSteps));
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[level-sim] {level.LevelId} slot-ascending run {r} threw: {e}");
                                ok = false;
                            }

                            yield return null;

                            if (!ok || WatchdogExpired())
                            {
                                ok = false;
                                break;
                            }
                        }
                    }

                    // Balance levels (TASK-023): the POSITIVE gate — synchronous pair-release (base one at a time,
                    // then symmetric load pairs bottom-up) must be reliable + deterministic (identical poseHash over
                    // repeats). The NEGATIVE gate needs no extra run: the release-order sweep above IS the "one piece
                    // at a time" case and on a balance level must be stably NOT Won (owner's winnability rule). QA
                    // bumps _repeats (≥20 for seesaws) for the reliability read.
                    if (ok && level.PhysicsMode == PhysicsMode.Balance)
                    {
                        for (var r = 0; r < _repeats; r++)
                        {
                            var sim = new UnityShapeFillSimulation(_tuning);
                            try
                            {
                                runs.Add(SimRun.Run(level, sim, new PairReleasePolicy(),
                                    "pair-release", "pair", r, _maxSteps));
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[level-sim] {level.LevelId} pair-release run {r} threw: {e}");
                                ok = false;
                            }

                            yield return null;

                            if (!ok || WatchdogExpired())
                            {
                                ok = false;
                                break;
                            }
                        }
                    }

                    if (!ok)
                        break;
                }
            }

            IReadOnlyList<LevelSolveStats> stats = Array.Empty<LevelSolveStats>();
            try
            {
                stats = SimAggregator.Aggregate(runs);
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] aggregation failed: {e}");
                ok = false;
            }

            object m0 = null;
            if (ok)
            {
                yield return StartCoroutine(BuildM0Summary(levels, runs, result => m0 = result));
                if (_m0Faulted)
                    ok = false;
            }

            var written = WriteReports(runs, stats, m0, out var dir, out var csvPath, out var jsonPath);
            LogSummary(stats, m0);

            // exitCode = tool health, NOT a finding: 0 ⇔ report on disk without exception; 1 ⇔ threw / no report /
            // watchdog. Solve-rate / solvability is DATA for balancer/QA, never a non-zero exit (RFC-Δ §4.2).
            var exitCode = ok && written ? 0 : 1;
            Debug.Log($"[level-sim] DONE exitCode={exitCode} reportDir={dir} " +
                      $"csv={csvPath ?? "<none>"} json={jsonPath ?? "<none>"}");
            LogSmoke(stats);

            if (_exitOnDone)
                Exit(exitCode);
        }

        // GAP-1: reuse the TASK-009 JSON pipeline verbatim (JsonLevelSource over the StreamingAssets ILevelStore,
        // same structural validation production runs) so the sweep drives the real two_cubes…snowman — no
        // duplicate loader. JsonLevelSource validates eagerly and throws on malformed content (fail-fast → exit 1).
        private static IReadOnlyList<LevelData> LoadLevels()
        {
            var source = new JsonLevelSource(new StreamingAssetsLevelStore(), new LevelDataValidator());
            var levels = new List<LevelData>(source.Count);
            for (var i = 0; i < source.Count; i++)
                levels.Add(source.Load(i));
            return levels;
        }

        private bool WatchdogExpired()
        {
            if (Time.realtimeSinceStartup - _startRealtime <= _batchTimeoutSeconds)
                return false;
            Debug.LogError($"[level-sim] watchdog: sweep exceeded {_batchTimeoutSeconds}s — aborting.");
            return true;
        }

        // M0-confirmation experiments (RFC §8): same-device reproducibility, safe acceleration factor,
        // match-tolerance headroom. Cross-device (M0.2 in the ADR) is a manual multi-device run, not here.
        private IEnumerator BuildM0Summary(
            IReadOnlyList<LevelData> levels, IReadOnlyList<SimRunResult> runs, Action<object> done)
        {
            var level = levels[0]; // the manifest's first level is the fixed m0 baseline (currently 'giraffe')
            var order = Identity(level.Pieces.Count);

            // M0.1 — same-device reproducibility: one fixed order, N fresh-instance repeats, count distinct hashes.
            var hashes = new HashSet<long>();
            for (var i = 0; i < _m0ReproRepeats; i++)
            {
                var sim = new UnityShapeFillSimulation(_tuning);
                try
                {
                    var res = SimRun.Run(level, sim, new ReleaseOrderPolicy((int[])order.Clone()),
                        "m0-repro", "identity", i, _maxSteps);
                    hashes.Add(res.PoseHash);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[level-sim] M0 repro repeat {i} threw: {e}");
                    _m0Faulted = true;
                }

                yield return null;
                if (_m0Faulted)
                    break;
            }
            var reproducible = hashes.Count == 1;

            // M0.2 — safe acceleration: the same order run all-in-one-frame (∞) vs spread across frames in
            // batches of stepsPerFrame must reach an identical final pose hash (RFC §8.2).
            long oneFrameHash = 0;
            try
            {
                oneFrameHash = RunOneFrameHash(level, (int[])order.Clone());
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] M0 one-frame reference threw: {e}");
                _m0Faulted = true;
            }
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

        // Small helper so the M0 one-frame reference reads by intent (and its SimRun.Run can be wrapped).
        private long RunOneFrameHash(LevelData level, int[] order)
        {
            var sim = new UnityShapeFillSimulation(_tuning);
            return SimRun.Run(level, sim, new ReleaseOrderPolicy(order), "m0-accel", "inf", 0, _maxSteps).PoseHash;
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

        private bool WriteReports(IReadOnlyList<SimRunResult> runs, IReadOnlyList<LevelSolveStats> stats, object m0,
            out string dir, out string csvPath, out string jsonPath)
        {
            csvPath = null;
            jsonPath = null;
            dir = ResolveOutputDir();

            try
            {
                var csv = SimReportWriter.BuildCsv(runs);
                // Provenance is captured from the tree the sweep actually ran in (repo root = the projectPath),
                // so a sim-worktree run stamps that worktree's detached ref, not the main tree's (TASK-058).
                var provenance = SimProvenance.Capture(ResolveRepoRoot(), _startWallClock);
                var json = SimReportWriter.BuildJson(runs, stats, m0, provenance);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

                Directory.CreateDirectory(dir);
                csvPath = Path.Combine(dir, $"level-sim_{stamp}.csv");
                jsonPath = Path.Combine(dir, $"level-sim_{stamp}.json");
                File.WriteAllText(csvPath, csv);
                File.WriteAllText(jsonPath, json);
                Debug.Log($"[level-sim] wrote report to {dir} (level-sim_{stamp}.csv / .json)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] failed to write report to {dir}: {e.Message}");
                csvPath = null;
                jsonPath = null;
                return false;
            }
        }

        private string ResolveOutputDir() => Path.Combine(ResolveRepoRoot(), _outputSubdir);

        // Editor/batch: repo root (parent of /Assets) — this is also the git worktree the sweep runs in, so it
        // doubles as the provenance CWD (TASK-058). Player build: persistentDataPath fallback (RFC §7 п.12).
        private static string ResolveRepoRoot()
        {
            var repoRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(repoRoot) ? Application.persistentDataPath : repoRoot;
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

        // Acceptance smoke (RFC-Δ §4.2 п.4): a stable grep line for QA/CI. Reports data; never gates exitCode.
        private static void LogSmoke(IReadOnlyList<LevelSolveStats> stats)
        {
            foreach (var s in stats)
            {
                if (s.LevelId != "two_cubes")
                    continue;
                Debug.Log($"[level-sim] SMOKE two_cubes deterministic={s.Deterministic} " +
                          $"solveRate={s.SolveRate.ToString("0.###", CultureInfo.InvariantCulture)}");
                return;
            }
            Debug.LogWarning("[level-sim] SMOKE two_cubes: not present in sweep stats.");
        }

        private static void Exit(int exitCode)
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.Exit(exitCode);
            else
                UnityEditor.EditorApplication.isPlaying = false; // interactive: just leave play mode
#else
            Application.Quit(exitCode);
#endif
        }

        // Placement indices of the TAPPABLE pieces (TASK-031 round-2): a preplaced lever/obstacle drops into the
        // physics at t=0 and is never tap-released, so it must not appear in a release order (video_launcher: the
        // stick is excluded → 3! = 6 orders over A/B/hammer). A level with no preplaced piece yields [0..count-1]
        // → identity mapping in BuildOrders → byte-for-byte the pre-TASK-031 order set (other levels untouched).
        private static IReadOnlyList<int> TappableIndices(LevelData level)
        {
            var indices = new List<int>(level.Pieces.Count);
            for (var i = 0; i < level.Pieces.Count; i++)
                if (!level.Pieces[i].Preplaced)
                    indices.Add(i);
            return indices;
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
