using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

        [Header("Timing pass (TASK-060; OPT-IN — separate report, NEVER judged by the gate)")]
        // The "fast"/impatient game: release the same orders WITHOUT waiting for rest, gap fixed steps apart. OFF
        // by default → the corpus sweep and the gated report are byte-for-byte unchanged. When on, its runs go to a
        // SEPARATE level-sim-timing report (not the gated one): a player playing fast is a balance/authoring FINDING,
        // never a red build (owner directive, TASK-060). Also flips on headlessly via env SIM_TIMING_GAP=<int> so a
        // batch probe needs no scene edit.
        [SerializeField] private bool _runImpatientPass = false;   // OFF by default → gated corpus sweep unchanged
        [SerializeField] private int _impatientGapSteps = 20;      // fixed steps between fast taps (~0.33s @60fps)
        // TASK-067: the timing pass ALSO replays each rest-gated POLICY's winning sequence (slot-ascending /
        // pair-release) at a human tempo — the coverage the enumerated-order impatient pass above misses for
        // levels solved by a POLICY, not a listed single order. These gaps are the acceptance table columns:
        // patient (from the gated sweep's policy runs) vs 8 / 20 / 32 fast steps. Tiny extra cost (1 sequence
        // per level per gap), so it never explodes the sweep. Off unless _runImpatientPass is on.
        [SerializeField] private int[] _policyScriptGaps = { 8, 20, 32 };

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

        // TASK-070/076: opt-in per-step [STEPLOG] pass for the device↔editor divergence diff. OFF by default → the
        // gated corpus sweep and every poseHash are byte-for-byte unchanged. Flips on headlessly via env
        // SIM_STEPLOG=<levelId> (run-sim.sh --steplog <levelId>), mirroring the SIM_TIMING_GAP opt-in, so the
        // committed scene stays default. TASK-076 generalised the pass from a hardcoded video_launcher / order-0-1-2
        // run to ANY campaign level: the env now carries the TARGET levelId and the pass runs a SINGLE dedicated
        // slot-ascending / patient run of THAT level AFTER the gated report is on disk, with the dev logger attached;
        // never touches the gated runs. null/empty → pass OFF.
        private string _stepLogLevelId;

        // TASK-179: opt-in TARGETED sweep. SIM_ONLY=<comma-separated levelIds> (set by run-sim.sh --only) restricts
        // the sweep to the named levels only — fast per-level physics iteration instead of judging the whole lineage,
        // which grows linearly with level count. null/empty → full sweep, byte-for-byte unchanged. A targeted run
        // writes a SEPARATE level-sim-only_<ts> report carrying a `scope` marker and SKIPS the M0 confirmation
        // (a lineage-level determinism/accel probe, not per-level), so it can never be mistaken for — nor gate — the
        // full lineage. An unknown id is a LOUD failure (never a silent empty sweep that reports PASS).
        private string _onlyLevelIds;

        private void Awake()
        {
            // Best-effort (RFC-Δ §7): if an interactive/MCP play-mode entered, keep the loop ticking without
            // focus so the coroutine can still finish and write the report.
            Application.runInBackground = true;

            // Headless batch MUST self-exit (do not trust the scene-serialized flag).
            if (Application.isBatchMode)
                _exitOnDone = true;

            // TASK-060: a headless timing probe flips the opt-in impatient pass on via env (no scene edit needed),
            // so the committed scene keeps the pass OFF and the gated sweep stays default. SIM_TIMING_GAP=<int>.
            var gapEnv = Environment.GetEnvironmentVariable("SIM_TIMING_GAP");
            if (Application.isBatchMode && int.TryParse(gapEnv, out var gap) && gap > 0)
            {
                _runImpatientPass = true;
                _impatientGapSteps = gap;
            }

            // TASK-179: headless opt-in for the targeted sweep (no scene edit; committed scene stays default),
            // mirroring the SIM_TIMING_GAP/SIM_STEPLOG opt-ins. SIM_ONLY carries the comma-separated target levelIds.
            var onlyEnv = Environment.GetEnvironmentVariable("SIM_ONLY");
            if (Application.isBatchMode && !string.IsNullOrEmpty(onlyEnv))
                _onlyLevelIds = onlyEnv;

#if GPIE_DEVELOPMENT
            // TASK-070/076: headless opt-in for the per-step logging pass (no scene edit; committed scene stays
            // default). Only meaningful under GPIE_DEVELOPMENT — the logger it drives is compiled out otherwise — and
            // the sim canonically runs in the editor, where GPIE_DEVELOPMENT is defined, so this is the real path.
            // TASK-076: SIM_STEPLOG now carries the TARGET levelId (was a bare on/off flag pinned to video_launcher);
            // a non-empty value enables the pass for THAT level. run-sim.sh --steplog <levelId> sets it.
            var stepLogLevel = Environment.GetEnvironmentVariable("SIM_STEPLOG");
            if (Application.isBatchMode && !string.IsNullOrEmpty(stepLogLevel))
                _stepLogLevelId = stepLogLevel;
#endif
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
            IReadOnlyList<TapSpacingAudit> tapAudits = Array.Empty<TapSpacingAudit>(); // TASK-181

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

            // TASK-179: restrict the sweep to SIM_ONLY (--only) before any run. An unknown id fails LOUDLY (ok=false
            // → exit 1) rather than sweeping an empty/short set that reports PASS — the worst outcome per the ticket.
            // run-sim.sh pre-validates ids against the snapshot before launching Unity; this is the SECOND line
            // (a real JSON parse vs the shell grep, and it reads the SAME manifest the batch actually loaded).
            if (ok && !string.IsNullOrEmpty(_onlyLevelIds))
            {
                var filtered = FilterToOnly(levels, _onlyLevelIds, out var missing);
                var available = new StringBuilder();
                foreach (var l in levels)
                    available.Append(available.Length == 0 ? string.Empty : ", ").Append(l.LevelId);
                if (missing.Count > 0)
                {
                    Debug.LogError($"[level-sim] --only: level(s) not found in campaign: {string.Join(", ", missing)} " +
                                   $"— NO sweep run. Available: {available}");
                    ok = false;
                }
                else if (filtered.Count == 0)
                {
                    Debug.LogError("[level-sim] --only: filter resolved to ZERO levels (SIM_ONLY empty after trim) " +
                                   $"— refusing an empty sweep. Available: {available}");
                    ok = false;
                }
                else
                {
                    Debug.Log($"[level-sim] --only: TARGETED sweep over {filtered.Count} level(s): " +
                              $"{string.Join(", ", ExtractLevelIds(filtered))} — PARTIAL report (level-sim-only_*), " +
                              "M0 skipped, NOT the lineage gate.");
                    levels = filtered;
                }
            }

            if (ok)
            {
                tapAudits = AuditTapSpacing(levels);
                foreach (var level in levels)
                {
                    // TASK-075: print the authored-touching classification of every slot pair (once per level) so QA
                    // can eyeball the DISJOINT/TOUCHING/OVERLAPPING split + the bimodal gap that justifies
                    // proximity_eps — before trusting the reference-rest floor. Diagnostic only; never gates.
                    LogTouchClassification(level);

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

            // TASK-075: fold the cross-run reference-rest floor into every run's honesty scalars BEFORE aggregation /
            // report write, so the gate (sim-gate.py reads MaxPenetration/IntersectingPairs) judges authored-touching
            // pairs against their settled honest rest, not against a physically-unreachable 0. Zero extra simulation.
            ApplyTouchingRestFloor(runs);

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

            // TASK-179: M0 confirmation (100-repeat determinism + accel experiments on levels[0]) is a lineage-level
            // platform probe (ADR-0001), not a per-level check — running it would defeat the whole point of a fast
            // targeted sweep. A partial run skips it (m0 stays null in the partial report; the `scope` marker records
            // why). The default full sweep is unchanged.
            var partial = !string.IsNullOrEmpty(_onlyLevelIds);
            object m0 = null;
            if (ok && !partial)
            {
                yield return StartCoroutine(BuildM0Summary(levels, runs, result => m0 = result));
                if (_m0Faulted)
                    ok = false;
            }
            else if (ok && partial)
            {
                Debug.Log("[level-sim] --only: M0 confirmation SKIPPED (lineage-level probe, not per-level).");
            }

            // TASK-181: fold the per-level tap-spacing audit into the report as a `tapAudit` block. Uses the default
            // TapZoneConfig — the SAME one AuditTapSpacing predicted with — for the corpus-level minTapDp/enforced
            // provenance. Null when the sweep produced no audits (e.g. load failed) so a broken run carries no
            // misleading empty block.
            object tapAudit = tapAudits.Count > 0 ? BuildTapAuditReport(new TapZoneConfig(), tapAudits) : null;

            // TASK-220 (§D16 phase 1): fold the per-level LOSEABILITY audit into the report as a `loseability` block —
            // sibling of tapAudit/stats/m0/runs. PURE aggregation of the runs already produced (per-order outcomes) —
            // ZERO extra simulation. Built ONLY for the FULL lineage sweep: the WARNING is position-gated ("позиции
            // 11+" in manifest order), which is meaningless over a --only subset, so a partial sweep carries no block
            // (loseability stays null), mirroring the M0 skip. WARNING-only — it never moves the verdict (sim-gate.py
            // reads only `runs`); threshold/error-mode is phase 2 after game-balancer calibrates per tier.
            object loseability = !partial && levels != null && runs.Count > 0
                ? BuildLoseabilityReport(levels, runs, UnloseablePositionThreshold)
                : null;
            var written = WriteReports(runs, stats, m0, tapAudit, loseability, partial,
                out var dir, out var csvPath, out var jsonPath);
            LogSummary(stats, m0);

            // exitCode = tool health, NOT a finding: 0 ⇔ report on disk without exception; 1 ⇔ threw / no report /
            // watchdog. Solve-rate / solvability is DATA for balancer/QA, never a non-zero exit (RFC-Δ §4.2).
            var exitCode = ok && written ? 0 : 1;
            Debug.Log($"[level-sim] DONE exitCode={exitCode} reportDir={dir} " +
                      $"csv={csvPath ?? "<none>"} json={jsonPath ?? "<none>"}");
            LogSmoke(stats);

            // TASK-060: OPT-IN "fast" game. Runs AFTER the gated report is on disk and into a SEPARATE report, so
            // the gate (sim-gate.py reads only the main report's runs) is untouched no matter what fast play finds.
            if (_runImpatientPass && ok && levels != null)
                yield return StartCoroutine(RunTimingPass(levels, runs));

            // TASK-070/076: OPT-IN per-step logging pass for the level named by SIM_STEPLOG. Also runs AFTER the gated
            // report is on disk and is purely additive (its own single run, dev logger attached) → the gated report
            // and every poseHash are unaffected regardless of the gate outcome.
            if (!string.IsNullOrEmpty(_stepLogLevelId) && levels != null)
                yield return StartCoroutine(RunStepLogPass(levels, _stepLogLevelId));

            if (_exitOnDone)
                Exit(exitCode);
        }

        // TASK-070/076: emit the per-step [STEPLOG] stream for ONE campaign level — the sim (editor=1) half of the
        // device↔editor divergence measurement, using the SAME StepPoseLogger the device APK carries, so the
        // editor/desktop lines are byte-identical to the device lines and diff line-by-line. The device half is
        // captured by the owner from logcat, tapping the same slot-ascending order. Additive and isolated: it never
        // runs during the gated sweep, so the gated report is unchanged; the logger is read-only, so even the numbers
        // it observes are the same trajectory the gate saw. Wrapped so a hiccup here can never wedge the finalised exit.
        // TASK-076: the level is <paramref name="levelId"/> (SIM_STEPLOG env), not the former hardcoded video_launcher,
        // and the order is the canonical rest-gated SlotAscendingPolicy — RNG-free and valid for ANY piece count,
        // replacing the video_launcher-only hardcoded 0-1-2 release order. Order barely affects the diff: the primary
        // probe is the FIRST released piece's free flight (a pure function of the static layout — see
        // sim-harness-metrics.md §(в)), which every deterministic order reaches identically.
        private IEnumerator RunStepLogPass(IReadOnlyList<LevelData> levels, string levelId)
        {
            LevelData level = null;
            foreach (var l in levels)
            {
                if (l.LevelId == levelId)
                {
                    level = l;
                    break;
                }
            }
            if (level == null)
            {
                // TASK-076: an unknown levelId must FAIL LOUDLY — never silently fall back to another level, or the
                // measurer diffs the wrong level's trajectory and never notices. This pass is additive (the gated
                // report is already on disk), so the error cannot move the gate verdict; the LogError + absent
                // [STEPLOG] lines ARE the visible signal in sim-run.log.
                var available = new StringBuilder();
                foreach (var l in levels)
                    available.Append(available.Length == 0 ? string.Empty : ", ").Append(l.LevelId);
                Debug.LogError($"[level-sim] STEPLOG ERROR: level '{levelId}' not found in campaign — no steplog " +
                               $"emitted. Available: {available}");
                yield break;
            }

            Debug.Log($"[level-sim] STEPLOG pass: {levelId} slot-ascending patient (editor half of the " +
                      "device↔editor diff) — SEPARATE from the gated sweep.");

            var sim = new UnityShapeFillSimulation(_tuning);
            try
            {
                // logSteps:true attaches the dev StepPoseLogger for THIS run only. SlotAscendingPolicy is the
                // canonical, RNG-free patient order (lowest target slot first) for any piece count; the measurer taps
                // the device half in the same order.
                SimRun.Run(level, sim, new SlotAscendingPolicy(),
                    "steplog", "slot-asc", 0, _maxSteps, logSteps: true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] STEPLOG pass threw: {e}");
            }

            yield return null; // RFC §6: let the deferred body Destroy flush
        }

        // TASK-060: the "impatient player" comparison. For every non-Balance level (Assembly + Catapult, where a tap
        // releases immediately, so "step T" == "release on step T"), re-runs the SAME release orders through
        // ScriptedReleasePolicy.Impatient(order, gap) — releasing the next piece WITHOUT waiting for rest — and
        // compares its final poses against the patient release-order runs already in <paramref name="patientRuns"/>.
        // Writes its own level-sim-timing_<ts> report (NEVER the gated one) and logs, per level, whether fast play
        // reaches a different outcome or different poses. Determinism is proven the same way as every other policy:
        // identical poseHash across the R repeats (timing is in fixed steps, no RNG/wall-clock).
        // TASK-067: after that enumerated loop it runs a SECOND, POLICY-SCRIPTED sub-pass (see the block comment
        // there) that replays each rest-gated policy's WINNING sequence — slot-ascending / pair-release — fast, at
        // every gap in _policyScriptGaps. That is the coverage the enumerated loop misses for levels whose solution
        // is a policy, not a listed order; both sub-passes write into the same (non-gated) timing report.
        private IEnumerator RunTimingPass(IReadOnlyList<LevelData> levels, IReadOnlyList<SimRunResult> patientRuns)
        {
            var gap = _impatientGapSteps;
            Debug.Log($"[level-sim] TIMING PASS (impatient, gapSteps={gap}) — SEPARATE report, not gated.");

            // Patient poseHash sets by (levelId, orderKey), from the release-order sweep already run.
            var patientHashes = new Dictionary<string, HashSet<long>>();
            var patientSolved = new Dictionary<string, int>();
            // TASK-067: patient POLICY baselines keyed by (policyId, levelId), from the gated sweep's
            // slot-ascending / pair-release runs — the "patient" column of the acceptance table.
            var patientPolicyHashes = new Dictionary<string, HashSet<long>>();
            var patientPolicySolved = new Dictionary<string, int>();
            foreach (var r in patientRuns)
            {
                if (r.PolicyId == "release-order")
                {
                    var key = r.LevelId + "|" + r.OrderKey;
                    if (!patientHashes.TryGetValue(key, out var set))
                        patientHashes[key] = set = new HashSet<long>();
                    set.Add(r.PoseHash);
                    if (r.Solved)
                        patientSolved[key] = patientSolved.TryGetValue(key, out var c) ? c + 1 : 1;
                }
                else if (r.PolicyId == "slot-ascending" || r.PolicyId == "pair-release")
                {
                    var key = r.PolicyId + "|" + r.LevelId;
                    if (!patientPolicyHashes.TryGetValue(key, out var set))
                        patientPolicyHashes[key] = set = new HashSet<long>();
                    set.Add(r.PoseHash);
                    if (r.Solved)
                        patientPolicySolved[key] = patientPolicySolved.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }

            var timingRuns = new List<SimRunResult>();
            var divergentLevels = new List<string>();

            foreach (var level in levels)
            {
                if (level.PhysicsMode == PhysicsMode.Balance)
                    continue; // balance inserts a coalescence window between tap and release; gap composes with it

                IReadOnlyList<int[]> orders;
                try
                {
                    orders = SolveSweepPlan.BuildOrders(
                        TappableIndices(level), _maxFullPermute, _sampleCount, level.Seed, null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[level-sim] timing {level.LevelId}: build orders threw: {e}");
                    continue;
                }

                var levelDiverged = false;
                foreach (var order in orders)
                {
                    var orderKey = string.Join("-", order);
                    var impatientHashes = new HashSet<long>();
                    var impatientSolved = 0;
                    for (var r = 0; r < _repeats; r++)
                    {
                        var sim = new UnityShapeFillSimulation(_tuning);
                        try
                        {
                            var res = SimRun.Run(level, sim, ScriptedReleasePolicy.Impatient(order, gap),
                                "impatient", orderKey, r, _maxSteps);
                            timingRuns.Add(res);
                            impatientHashes.Add(res.PoseHash);
                            if (res.Solved)
                                impatientSolved++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[level-sim] timing {level.LevelId} order {orderKey} run {r} threw: {e}");
                        }

                        yield return null;
                        if (WatchdogExpired())
                            break;
                    }

                    var pKey = level.LevelId + "|" + orderKey;
                    patientHashes.TryGetValue(pKey, out var pHashes);
                    var pSolved = patientSolved.TryGetValue(pKey, out var ps) ? ps : 0;
                    var impatientDeterministic = impatientHashes.Count == 1;
                    // "Different physics": a different solved-count (outcome flip) OR a disjoint pose-hash set.
                    var outcomeDiffers = impatientSolved != pSolved;
                    var posesDiffer = pHashes != null && !pHashes.SetEquals(impatientHashes);
                    if (outcomeDiffers || posesDiffer)
                        levelDiverged = true;

                    Debug.Log($"[level-sim] TIMING {level.LevelId} order={orderKey} gap={gap}: " +
                              $"impatient solved {impatientSolved}/{_repeats} (patient {pSolved}/{_repeats}), " +
                              $"impatientDeterministic={impatientDeterministic} " +
                              $"impatientHashes={impatientHashes.Count} patientHashes={pHashes?.Count ?? 0} " +
                              $"outcomeDiffers={outcomeDiffers} posesDiffer={posesDiffer}");

                    if (WatchdogExpired())
                        break;
                }

                if (levelDiverged)
                    divergentLevels.Add(level.LevelId);
                if (WatchdogExpired())
                    break;
            }

            // TASK-067: POLICY-SCRIPTED sub-pass — the coverage the enumerated loop above misses. A level solved
            // by a POLICY (slot-ascending assembly / pair-release balance) rather than a listed single order was
            // never measured fast: the loop above re-runs only SolveSweepPlan's orders, which for a seeded-sampled
            // figure or a seesaw are NOT the policy's sequence. Here we replay the EXACT sequence each rest-gated
            // policy chooses (PolicyScriptPlan mirrors the policy) but at a human tempo — a fixed gap between
            // releases instead of waiting for global rest — over every gap in _policyScriptGaps, so one sweep
            // yields the whole acceptance table (patient vs 8/20/32). Balance → scripted pair-release (pairs stay
            // synchronous, groups go back-to-back — never split); every other mode → scripted slot-ascending.
            // Catapult is INCLUDED under slot-ascending only for parity with its patient slot-ascending smoke; its
            // real solution is a bespoke charge chain, so a loss there is expected and not a regression signal.
            var policyRegressions = new List<string>();
            Debug.Log($"[level-sim] TIMING POLICY-SCRIPTED pass gaps=[{string.Join(",", _policyScriptGaps)}] " +
                      "— SEPARATE report, not gated.");
            foreach (var level in levels)
            {
                var balance = level.PhysicsMode == PhysicsMode.Balance;
                var patientPolicyId = balance ? "pair-release" : "slot-ascending";
                var fastPolicyId = balance ? "fast-pair" : "fast-slot-asc";
                var pKey = patientPolicyId + "|" + level.LevelId;
                patientPolicyHashes.TryGetValue(pKey, out var pHashes);
                var pSolved = patientPolicySolved.TryGetValue(pKey, out var ps) ? ps : 0;

                foreach (var policyGap in _policyScriptGaps)
                {
                    if (policyGap <= 0)
                        continue;

                    var script = balance
                        ? PolicyScriptPlan.PairRelease(level, policyGap)
                        : PolicyScriptPlan.SlotAscending(level, policyGap);
                    var orderKey = "gap-" + policyGap;
                    var fastHashes = new HashSet<long>();
                    var fastSolved = 0;
                    for (var r = 0; r < _repeats; r++)
                    {
                        var sim = new UnityShapeFillSimulation(_tuning);
                        try
                        {
                            var res = SimRun.Run(level, sim, new ScriptedReleasePolicy(script),
                                fastPolicyId, orderKey, r, _maxSteps);
                            timingRuns.Add(res);
                            fastHashes.Add(res.PoseHash);
                            if (res.Solved)
                                fastSolved++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[level-sim] timing-policy {level.LevelId} {fastPolicyId} {orderKey} " +
                                           $"run {r} threw: {e}");
                        }

                        yield return null;
                        if (WatchdogExpired())
                            break;
                    }

                    var deterministic = fastHashes.Count == 1;
                    var posesDiffer = pHashes != null && !pHashes.SetEquals(fastHashes);
                    // A regression is losing winnability the patient policy HAD (pSolved>0 → fastSolved==0). A level
                    // the patient policy never solved (pSolved==0) is "still not covered", not "broke" — the caller
                    // separates the two in the artifact table.
                    if (pSolved > 0 && fastSolved == 0)
                        policyRegressions.Add(level.LevelId + "@" + policyGap);

                    Debug.Log($"[level-sim] TIMING-POLICY {level.LevelId} policy={fastPolicyId} gap={policyGap}: " +
                              $"fast solved {fastSolved}/{_repeats} (patient {patientPolicyId} {pSolved}/{_repeats}), " +
                              $"deterministic={deterministic} fastHashes={fastHashes.Count} " +
                              $"patientHashes={pHashes?.Count ?? 0} posesDiffer={posesDiffer}");

                    if (WatchdogExpired())
                        break;
                }

                if (WatchdogExpired())
                    break;
            }
            Debug.Log($"[level-sim] TIMING POLICY SUMMARY gaps=[{string.Join(",", _policyScriptGaps)}] " +
                      $"lost-winnability={policyRegressions.Count} {string.Join(",", policyRegressions)}");

            // TASK-075: apply the same reference-rest floor to the timing runs so their honesty FYI is measured on the
            // right baseline too (its floor is MIN over the timing pass's OWN honest wins — a fast run is not a
            // reference for the gated sweep and vice-versa). Still FYI-only: the timing report is never gated.
            ApplyTouchingRestFloor(timingRuns);

            // Honesty of the fast game is DATA, not a gate: report int/pen counts so a finding is visible. TASK-343:
            // the unsup-won count was removed with the UnsupportedPlaced metric (a directional rudiment post-TASK-305).
            var timingInt = 0;
            var timingPenWon = 0;
            foreach (var r in timingRuns)
            {
                if (r.IntersectingPairs > 0) timingInt++;
                if (r.Solved && r.MaxPenetration > 1e-4f) timingPenWon++;
            }
            Debug.Log($"[level-sim] TIMING SUMMARY gap={gap} runs={timingRuns.Count} " +
                      $"divergent-levels={divergentLevels.Count} {string.Join(",", divergentLevels)} " +
                      $"| fast-honesty(FYI, NOT gated): int={timingInt} pen-won={timingPenWon}");

            WriteTimingReport(timingRuns);
        }

        // Writes the timing pass to its OWN report file so it never mixes into the gated report. Reuses the shared
        // writer/aggregator; failure to write is logged and does not affect the gate (already on disk).
        private void WriteTimingReport(IReadOnlyList<SimRunResult> timingRuns)
        {
            if (timingRuns.Count == 0)
            {
                Debug.LogWarning("[level-sim] timing pass produced no runs — nothing to write.");
                return;
            }
            try
            {
                var stats = SimAggregator.Aggregate(timingRuns);
                var provenance = SimProvenance.Capture(ResolveRepoRoot(), _startWallClock);
                var json = SimReportWriter.BuildJson(timingRuns, stats, null, provenance);
                var dir = ResolveOutputDir();
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"level-sim-timing_{stamp}.json");
                File.WriteAllText(path, json);
                Debug.Log($"[level-sim] wrote TIMING report to {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[level-sim] failed to write timing report: {e.Message}");
            }
        }

        // TASK-075 reference-rest baseline: fold the cross-run honest floor of every authored-TOUCHING pair into each
        // run's honesty scalars. PURE post-processing of runs already produced — ZERO extra simulation (the answer to
        // QA's "does it double the sweep?": no). Per level: floor[(lo,hi)] = MIN, over the level's HONEST WINS
        // (Solved ∧ IntersectingPairs==0 — neither of which depends on the touching-pen verdict, so there is no
        // circularity; TASK-343 dropped the UnsupportedPlaced==0 term with the metric), of that pair's raw resting
        // penetration. Then EVERY run's scalars are rebuilt by
        // folding max(raw − floor, 0) over its touching pairs back into MaxPenetration / IntersectingPairs / WorstPair.
        // A pair that never appears in a reference win (no honest run mapped pieces into both its slots), or a level
        // with NO honest win, floors at 0 → judged raw (strictest). Runs with no touching pair are left untouched, so
        // non-circle levels stay byte-for-byte identical. Mutates the list in place (readonly struct → replace slot).
        private static void ApplyTouchingRestFloor(List<SimRunResult> runs)
        {
            if (runs == null || runs.Count == 0)
                return;

            // Pass 1 — per level, per normalised slot pair, the MIN honest-win raw penetration = the floor.
            var floorByLevel = new Dictionary<string, Dictionary<long, float>>();
            foreach (var run in runs)
            {
                if (!(run.Solved && run.IntersectingPairs == 0))
                    continue; // reference set = honest wins only (the safe MIN, RFC §2.2)
                if (run.TouchingPens == null || run.TouchingPens.Count == 0)
                    continue;

                if (!floorByLevel.TryGetValue(run.LevelId, out var floor))
                    floorByLevel[run.LevelId] = floor = new Dictionary<long, float>();
                foreach (var tp in run.TouchingPens)
                {
                    var key = PairKey(tp.SlotA, tp.SlotB);
                    if (!floor.TryGetValue(key, out var cur) || tp.Penetration < cur)
                        floor[key] = tp.Penetration;
                }
            }

            // Pass 2 — correct EVERY run (reference or not) against its level's floor.
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                if (run.TouchingPens == null || run.TouchingPens.Count == 0)
                    continue; // no authored-touching pair here → scalars already final

                floorByLevel.TryGetValue(run.LevelId, out var floor);
                var maxPen = run.MaxPenetration;
                var intPairs = run.IntersectingPairs;
                var worst = run.WorstPair;
                foreach (var tp in run.TouchingPens)
                {
                    var f = 0f;
                    if (floor != null && floor.TryGetValue(PairKey(tp.SlotA, tp.SlotB), out var ff))
                        f = ff;
                    var overshoot = tp.Penetration - f;
                    if (overshoot < 0f)
                        overshoot = 0f;
                    if (overshoot > maxPen)
                    {
                        maxPen = overshoot;
                        worst = new WorstOverlapPair(tp.KindA, tp.KindB, tp.Penetration, f, tp.SlotA, tp.SlotB);
                    }
                    if (overshoot > SimRun.DetectorEpsilon)
                        intPairs++;
                }
                runs[i] = run.WithCorrectedHonesty(intPairs, maxPen, worst);
            }
        }

        // Order-independent key for a slot pair (lo<<32 | hi) — a run may map the two pieces to the pair in either
        // order, so the floor must key on the unordered pair.
        private static long PairKey(int a, int b)
        {
            var lo = a < b ? a : b;
            var hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        // TASK-075 corpus-validation (owner directive / RFC acceptance §5): print every slot pair's class + signed gap
        // for one level, sorted by gap, so QA confirms the bimodal split (a touching cluster at ≈0, a disjoint cluster
        // ≫ eps, an EMPTY band between where proximity_eps sits) with its own eyes rather than trusting the number.
        // Especially the castle_gate roof/bigcircle circle-poly pair the owner flagged (deep overlap ⇒ must read
        // OVERLAPPING, not TOUCHING, else it wrongly gets a dynamic floor). Diagnostic; never gates.
        private static void LogTouchClassification(LevelData level)
        {
            var slots = level.Target?.Slots;
            if (slots == null || slots.Count == 0)
                return;

            var shapeByKind = new Dictionary<string, Shape>();
            CollectShapes(level.Pieces, shapeByKind);
            CollectShapes(level.Decoys, shapeByKind);

            var pairs = SlotTouchClassifier.ClassifyLevel(slots, shapeByKind);
            var sorted = new List<SlotPairTouch>(pairs);
            sorted.Sort((x, y) => x.Separation.CompareTo(y.Separation));

            var sb = new StringBuilder();
            sb.Append($"[level-sim] TOUCH-CLASSIFY {level.LevelId}: {pairs.Count} slot-pairs " +
                      $"(proximity_eps={SlotTouchClassifier.ProximityEps.ToString("0.#####", CultureInfo.InvariantCulture)})");
            var touchingMax = float.NegativeInfinity;
            var disjointMin = float.PositiveInfinity;
            foreach (var p in sorted)
            {
                var sepStr = p.Separation >= float.MaxValue
                    ? "apart(poly)"
                    : p.Separation.ToString("+0.#####;-0.#####;0", CultureInfo.InvariantCulture);
                sb.Append($"\n   sep={sepStr,-12} {p.Class,-11} " +
                          $"{slots[p.SlotA].Kind}[{p.SlotA}]/{slots[p.SlotB].Kind}[{p.SlotB}]" +
                          (p.CircleContaining ? "" : " (poly-poly, floor N/A)"));
                if (p.Class == SlotTouchClass.Touching && p.Separation > touchingMax)
                    touchingMax = p.Separation;
                if (p.Class == SlotTouchClass.Disjoint && p.Separation < disjointMin && p.Separation < float.MaxValue)
                    disjointMin = p.Separation;
            }
            if (touchingMax > float.NegativeInfinity && disjointMin < float.PositiveInfinity)
            {
                var eps = SlotTouchClassifier.ProximityEps;
                var inEmptyBand = eps > touchingMax && eps < disjointMin;
                sb.Append($"\n   bimodal: touching-max={touchingMax.ToString("0.#####", CultureInfo.InvariantCulture)} " +
                          $"disjoint-min={disjointMin.ToString("0.#####", CultureInfo.InvariantCulture)} " +
                          $"gap={(disjointMin - touchingMax).ToString("0.#####", CultureInfo.InvariantCulture)} " +
                          $"(eps in empty band: {(inEmptyBand ? "YES" : "CHECK")})");
            }
            Debug.Log(sb.ToString());
        }

        private static void CollectShapes(IReadOnlyList<PiecePlacement> pieces, Dictionary<string, Shape> shapeByKind)
        {
            if (pieces == null)
                return;
            foreach (var p in pieces)
                if (p.Type != null && !shapeByKind.ContainsKey(p.Type.Kind))
                    shapeByKind[p.Type.Kind] = p.Type.Shape;
        }

        // GAP-1: reuse the TASK-009 JSON pipeline verbatim (JsonLevelSource over the StreamingAssets ILevelStore,
        // same structural validation production runs) so the sweep drives the real two_cubes…snowman — no
        // duplicate loader. JsonLevelSource validates eagerly and throws on malformed content (fail-fast → exit 1).
        // TASK-085: enumerate every campaign level through the tap-spacing check and log each warning, so the
        // author-time audit of "tapable pieces closer than the min tap zone" falls out of the normal sweep (RFC §7's
        // sanctioned enumerate-all-levels method). Log-only for the gate; never gates (the check is a WARNING in
        // phase 1). Uses a fresh validator with the default TapZoneConfig — the SAME reference-device prediction the
        // shipped validator emits at load — so the count here is exactly what LevelDataValidator would report.
        // TASK-181: the audit is now ALSO returned as data (one TapSpacingAudit per level) so WriteReports can lift it
        // into the report's `tapAudit` block instead of only the log. Both channels derive from the SAME
        // LevelDataValidator.AuditTapSpacing geometry, so the [TAP-AUDIT] line count can never drift from
        // tapAudit.totalViolations (owner's parity check). This pass is now SCOPED to tap spacing: the [TAP-AUDIT]
        // prefix and the total count only ever cover "Tap spacing" warnings. Other validator warnings (e.g. the
        // TASK-107 air-zone hover-guard bypass on air_dunk) previously rode under [TAP-AUDIT] incidentally — they now
        // log under [LEVEL-WARN] so no sweep signal is lost (JsonLevelSource discards warnings, so this pass is their
        // only surfacing) without inflating the tap-spacing count.
        private static IReadOnlyList<TapSpacingAudit> AuditTapSpacing(IReadOnlyList<LevelData> levels)
        {
            var validator = new LevelDataValidator(new TapZoneConfig());
            var audits = new List<TapSpacingAudit>(levels.Count);
            var total = 0;
            foreach (var level in levels)
            {
                audits.Add(validator.AuditTapSpacing(level));

                // Log channel: reuse the validator's own warning STRINGS (byte-identical [TAP-AUDIT] lines to
                // TASK-085) while the structured block above comes from the same geometry — one source of truth. A
                // "Tap spacing" warning is a tap violation (counted); anything else is a different check, surfaced
                // under [LEVEL-WARN] so it stays visible without polluting the tap-spacing count.
                var result = validator.Validate(level);
                foreach (var w in result.Warnings)
                {
                    if (w.StartsWith("Tap spacing:", StringComparison.Ordinal))
                    {
                        total++;
                        Debug.Log($"[TAP-AUDIT] {level.LevelId}: {w}");
                    }
                    else
                    {
                        Debug.Log($"[LEVEL-WARN] {level.LevelId}: {w}");
                    }
                }
            }
            Debug.Log($"[TAP-AUDIT] total tap-spacing warnings across {levels.Count} level(s): {total}");
            return audits;
        }

        // TASK-181: assemble the machine-readable `tapAudit` block (sibling of stats/m0/runs in the JSON report) from
        // the per-level structural audits. minTapDp/enforcedAsError are corpus-level provenance (which phase, what
        // threshold produced the numbers); totalViolations is the sum across levels — and because both it and the
        // [TAP-AUDIT] log lines derive from LevelDataValidator.AuditTapSpacing, it equals the [TAP-AUDIT] line count.
        // Every swept level gets an entry even when clean (violations: []), so "no entry" (level not swept) is
        // distinguishable from "0 violations". Serialised only when non-null → the timing report is unaffected.
        private static object BuildTapAuditReport(TapZoneConfig config, IReadOnlyList<TapSpacingAudit> audits)
        {
            var total = 0;
            var levels = new List<object>(audits.Count);
            foreach (var a in audits)
            {
                var violations = new List<object>(a.Violations.Count);
                foreach (var v in a.Violations)
                {
                    total++;
                    violations.Add(new
                    {
                        indexA = v.IndexA,
                        kindA = v.KindA,
                        indexB = v.IndexB,
                        kindB = v.KindB,
                        distance = v.Distance,
                    });
                }
                levels.Add(new
                {
                    levelId = a.LevelId,
                    minTapWorld = a.MinTapWorld,
                    orthoSize = a.OrthoSize,
                    violations,
                });
            }
            return new
            {
                minTapDp = config.MinTapDp,
                enforcedAsError = config.EnforceMinDistanceAsError,
                totalViolations = total,
                levels,
            };
        }

        // TASK-220 (§D16 phase 1): the 1-based manifest position from which an unloseable level (orderWinRate==1.0)
        // is WARNED. Early levels (positions 1..10) are ALLOWED to be unloseable — they teach the mechanic — so the
        // flag only fires from position 11. Corpus-level provenance, stamped into the report's `loseability` block.
        private const int UnloseablePositionThreshold = 11;

        // TASK-220 (§D16 phase 1): assemble the machine-readable `loseability` block (sibling of tapAudit/stats/m0/runs)
        // from the release-order sweep runs already produced — ZERO extra simulation. For each level, over its
        // release-order permutations ONLY (the "player picks an arbitrary order" cases; the intended-solution policies
        // slot-ascending / pair-release ARE meant to win and are EXCLUDED — else every level would read winnable and
        // the metric would be blind), count how many orders WIN (all repeats Solved; the sim is deterministic so an
        // order wins-all or loses-all, and a split repeat reads as not-won → the level stays "loseable", the safe
        // direction). orderWinRate==1.0 means EVERY arbitrary release order wins ⇒ the player literally cannot lose.
        // Healthy lineage references: ball_cradle 0/61 (only the policy wins), balance levels 0/N (§L9 negative gate);
        // the three rejected campaigns read 1.0 ("wins any order") — the "too easy" signal the owner flagged three
        // times. `loseable` = at least one arbitrary order loses. `unloseable` = orderWinRate==1.0 AND position ≥
        // threshold. Logs a [LOSEABILITY] line per flagged level + a one-line corpus summary so the orchestrator sees
        // it without reading JSON; and unloseableCount equals the number of ⚠ lines (parity, like tapAudit). Every
        // swept level gets an entry (so "no entry" = "not swept", not "0 orders"). timingSensitive is null in phase 1:
        // it needs the OPT-IN impatient pass (a SEPARATE report, absent from the gated runs) — phase 2 wires it once
        // game-balancer calibrates the per-tier thresholds.
        private static object BuildLoseabilityReport(
            IReadOnlyList<LevelData> levels, IReadOnlyList<SimRunResult> runs, int positionThreshold)
        {
            // Per level → per release-order OrderKey → [repeats, solvedRepeats]. Only PolicyId=="release-order".
            var byLevel = new Dictionary<string, Dictionary<string, int[]>>();
            foreach (var r in runs)
            {
                if (r.PolicyId != "release-order")
                    continue;
                if (!byLevel.TryGetValue(r.LevelId, out var orders))
                    byLevel[r.LevelId] = orders = new Dictionary<string, int[]>();
                if (!orders.TryGetValue(r.OrderKey, out var tally))
                    orders[r.OrderKey] = tally = new int[2];
                tally[0]++;
                if (r.Solved)
                    tally[1]++;
            }

            var levelReports = new List<object>(levels.Count);
            var eligible = 0;          // levels at position ≥ threshold — the set the summary is "of"
            var unloseableCount = 0;
            for (var i = 0; i < levels.Count; i++)
            {
                var position = i + 1;  // 1-based manifest position
                byLevel.TryGetValue(levels[i].LevelId, out var orders);
                var ordersTried = orders?.Count ?? 0;
                var ordersWon = 0;
                if (orders != null)
                    foreach (var t in orders.Values)
                        if (t[0] > 0 && t[1] == t[0])
                            ordersWon++;
                var orderWinRate = ordersTried == 0 ? 0f : (float)ordersWon / ordersTried;
                var loseable = ordersWon < ordersTried; // at least one arbitrary order loses
                var atOrPastThreshold = position >= positionThreshold;
                if (atOrPastThreshold)
                    eligible++;
                // ordersTried>0 guards the degenerate all-preplaced level (no release order) out of the flag.
                var unloseable = atOrPastThreshold && ordersTried > 0 && ordersWon == ordersTried;
                if (unloseable)
                {
                    unloseableCount++;
                    Debug.Log($"[LOSEABILITY] {levels[i].LevelId} (pos {position}): orderWinRate=1.0 " +
                              $"({ordersWon}/{ordersTried} orders won) ⚠ unloseable — every arbitrary release order wins.");
                }
                levelReports.Add(new
                {
                    levelId = levels[i].LevelId,
                    position,
                    ordersTried,
                    ordersWon,
                    orderWinRate,
                    loseable,
                    timingSensitive = (bool?)null, // phase 2: needs the opt-in impatient pass (separate report)
                    unloseable,
                });
            }

            // Console summary (RFC-style grep line): the one-liner the orchestrator reads without opening JSON.
            Debug.Log($"[level-sim] LOSEABILITY: {unloseableCount} unloseable of {eligible} " +
                      $"(позиции {positionThreshold}+)");

            return new
            {
                positionThreshold,
                unloseableCount,
                eligibleLevels = eligible,
                levels = levelReports,
            };
        }

        // TASK-179: restrict a loaded campaign to the SIM_ONLY level ids (targeted sweep). Returns the surviving
        // levels in manifest order; any requested id absent from the campaign is collected into <paramref
        // name="missing"/> so the caller fails loudly instead of silently sweeping a short/empty set. Duplicate and
        // blank ids in the CSV are ignored; matching is exact on LevelId.
        private static IReadOnlyList<LevelData> FilterToOnly(
            IReadOnlyList<LevelData> all, string csv, out List<string> missing)
        {
            var wanted = new List<string>();
            foreach (var raw in csv.Split(','))
            {
                var id = raw.Trim();
                if (id.Length > 0 && !wanted.Contains(id))
                    wanted.Add(id);
            }

            var present = new HashSet<string>();
            foreach (var l in all)
                present.Add(l.LevelId);

            missing = new List<string>();
            foreach (var id in wanted)
                if (!present.Contains(id))
                    missing.Add(id);

            var keep = new HashSet<string>(wanted);
            var filtered = new List<LevelData>();
            foreach (var l in all)
                if (keep.Contains(l.LevelId))
                    filtered.Add(l);
            return filtered;
        }

        private static List<string> ExtractLevelIds(IReadOnlyList<LevelData> levels)
        {
            var ids = new List<string>(levels.Count);
            foreach (var l in levels)
                ids.Add(l.LevelId);
            return ids;
        }

        private static IReadOnlyList<LevelData> LoadLevels()
        {
            var source = new JsonLevelSource(new StreamingAssetsLevelStore(), new LevelDataValidator(new TapZoneConfig()));
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
            var level = levels[0]; // the manifest's first level is the fixed m0 baseline (currently 'sailboat' since TASK-100 reordered the lineup; was 'giraffe'). m0 is a same-device DETERMINISM probe on whatever levels[0] is — it counts distinct poseHashes across repeats, not a comparison to a stored baseline — so the reorder just re-baselines it onto sailboat, it does not fail the gate.
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
            object tapAudit, object loseability, bool partial, out string dir, out string csvPath, out string jsonPath)
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
                // TASK-179: a targeted (--only) sweep writes a SEPARATE level-sim-only_<ts> report (the `-` after
                // "level-sim" keeps it OUT of the lineage gate's `level-sim_*.json` glob, same convention as the
                // timing report) AND embeds a `scope` marker so sim-gate.py stamps the verdict PARTIAL. The default
                // full sweep gets scope==null → byte-for-byte identical report as before.
                object scope = null;
                var prefix = "level-sim";
                if (partial)
                {
                    var swept = new List<string>();
                    foreach (var r in runs)
                        if (!swept.Contains(r.LevelId))
                            swept.Add(r.LevelId);
                    scope = new
                    {
                        kind = "partial",
                        onlyLevels = swept,
                        note = "TASK-179 targeted sweep (--only): judges ONLY these levels; NOT the full-lineage " +
                               "gate. M0 confirmation skipped.",
                    };
                    prefix = "level-sim-only";
                }
                var json = SimReportWriter.BuildJson(runs, stats, m0, provenance, tapAudit, scope, loseability);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

                Directory.CreateDirectory(dir);
                csvPath = Path.Combine(dir, $"{prefix}_{stamp}.csv");
                jsonPath = Path.Combine(dir, $"{prefix}_{stamp}.json");
                File.WriteAllText(csvPath, csv);
                File.WriteAllText(jsonPath, json);
                Debug.Log($"[level-sim] wrote report to {dir} ({prefix}_{stamp}.csv / .json)");
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
