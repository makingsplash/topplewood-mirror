#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Gameplay.Sim.Editor
{
    /// <summary>
    /// TASK-332 — headless standing regression for <see cref="LevelDataValidator"/>. Runs every fixture in
    /// <c>.studio/tools/tests/*.json</c> that DECLARES an expected verdict through the SHIPPING validator and asserts
    /// the verdict matches — the automation the corpus never had (a validator rule that "stopped catching a broken
    /// mosaic" was caught by the owner's eye, last in the chain; TASK-318 shipped a physically un-buildable target
    /// through compile, parse, validate, load AND sim). Invoked (mirrors <see cref="LevelFamilyGenerator"/>) by
    /// <c>.studio/tools/validate-fixtures.sh</c>:
    /// <code>&lt;Unity&gt; -batchmode -projectPath &lt;repo&gt; -executeMethod Gameplay.Sim.Editor.ValidateFixturesBatch.Run</code>
    ///
    /// Fixture convention: a level object may carry <c>"expectValidator": "reject" | "accept"</c> (a level WITHOUT it
    /// is a fixture for another oracle — e.g. the python tiling self-test — and is SKIPPED here, not failed) and,
    /// for a reject, an optional <c>"expectError"</c> substring that must appear among the errors (so a fixture proves
    /// the rule rejects for the RIGHT reason, not incidentally). Newtonsoft ignores these extra fields when the same
    /// file is deserialised as a <see cref="CampaignFileDto"/> for the domain, so the fixtures stay loadable by the game.
    ///
    /// FALSE-GREEN GUARD (the load-bearing invariant, per the ticket): agreement on the live corpus is agreement on
    /// the EMPTY set (there it all skips), so the gate FAILS unless at least one RED (reject) fixture was actually
    /// exercised and correctly rejected. A gate that only ever sees green fixtures is green always. Exit: 0 = every
    /// declared verdict matched AND ≥1 red asserted; 1 = a mismatch or the false-green guard tripped; 2 = bad config
    /// (fixtures dir missing); 3 = a fixture threw before the validator could judge it.
    /// </summary>
    public static class ValidateFixturesBatch
    {
        public static void Run()
        {
            var code = 3;
            try { code = Execute(); }
            catch (Exception e) { Debug.LogError($"[validate-fixtures] FATAL: {e.Message}\n{e.StackTrace}"); code = 3; }
            EditorApplication.Exit(code);
        }

        private static int Execute()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var dir = Environment.GetEnvironmentVariable("VALIDATE_FIXTURES_DIR");
            dir = string.IsNullOrWhiteSpace(dir)
                ? Path.Combine(projectRoot, ".studio", "tools", "tests")
                : (Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(projectRoot, dir)));

            if (!Directory.Exists(dir))
            {
                Debug.LogError($"[validate-fixtures] fixtures dir not found: {dir}");
                return 2;
            }

            var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Debug.Log($"[validate-fixtures] scanning {files.Length} fixture file(s) in {dir}");

            var asserted = 0;   // levels that carried an expectValidator (were actually judged)
            var reds = 0;       // reject-expecting levels that were correctly rejected for the right reason
            var mismatches = new List<string>();
            var fatal = false;

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                CampaignFileDto campaign;
                ExpectFileDto expect;
                try
                {
                    var text = File.ReadAllText(file);
                    campaign = JsonConvert.DeserializeObject<CampaignFileDto>(text);
                    expect = JsonConvert.DeserializeObject<ExpectFileDto>(text);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[validate-fixtures] {name}: could not parse fixture — {e.Message}");
                    fatal = true;
                    continue;
                }

                var expectations = BuildExpectations(expect);
                if (campaign?.Levels == null || campaign.Levels.Count == 0)
                {
                    if (expectations.Count > 0)
                    {
                        Debug.LogError($"[validate-fixtures] {name}: declares expectValidator but has no parseable levels");
                        fatal = true;
                    }
                    else
                    {
                        Debug.Log($"[validate-fixtures] {name}: no expectValidator anywhere — SKIP (another oracle's fixture)");
                    }
                    continue;
                }

                var validator = new LevelDataValidator(new TapZoneConfig());
                foreach (var dto in campaign.Levels)
                {
                    var id = dto?.LevelId ?? "(no id)";
                    if (dto == null || !expectations.TryGetValue(dto.LevelId ?? "", out var want))
                    {
                        Debug.Log($"[validate-fixtures] {name}:{id}: no expectValidator — SKIP");
                        continue;
                    }

                    asserted++;
                    LevelData domain;
                    try { domain = dto.ToDomain(); }
                    catch (Exception e)
                    {
                        // The validator is the unit under test; a fixture must PARSE to reach it. A ToDomain throw
                        // means the fixture is malformed, not that the validator rejected it — so it is a mismatch.
                        mismatches.Add($"{name}:{id}: expected {want.Verdict} but ToDomain threw before the validator ran — {e.Message}");
                        continue;
                    }

                    var result = validator.Validate(domain);
                    if (want.Verdict == "accept")
                    {
                        if (result.Ok)
                            Debug.Log($"[validate-fixtures] {name}:{id}: accept ✓");
                        else
                            mismatches.Add($"{name}:{id}: expected ACCEPT but validator REJECTED — {string.Join("; ", result.Errors)}");
                        continue;
                    }

                    // want.Verdict == "reject"
                    if (result.Ok)
                    {
                        mismatches.Add($"{name}:{id}: expected REJECT but validator ACCEPTED it — the rule does not catch this fixture");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(want.ErrorSubstring) &&
                        !result.Errors.Any(e => e.IndexOf(want.ErrorSubstring, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        mismatches.Add($"{name}:{id}: rejected, but NOT for the expected reason '{want.ErrorSubstring}' — got: {string.Join("; ", result.Errors)}");
                        continue;
                    }
                    reds++;
                    Debug.Log($"[validate-fixtures] {name}:{id}: reject ✓ ({string.Join("; ", result.Errors)})");
                }
            }

            Debug.Log($"[validate-fixtures] asserted {asserted} level(s): reds {reds}, mismatches {mismatches.Count}");
            foreach (var m in mismatches)
                Debug.LogError($"[validate-fixtures] MISMATCH: {m}");

            if (fatal)
                return 3;

            if (asserted == 0)
            {
                Debug.LogError("[validate-fixtures] FALSE-GREEN: no fixture declared an expectValidator — the gate " +
                               "judged nothing. Add at least one RED (\"expectValidator\":\"reject\") fixture.");
                return 1;
            }
            if (reds == 0)
            {
                Debug.LogError("[validate-fixtures] FALSE-GREEN: not one RED fixture was exercised — agreement on only " +
                               "green/accept fixtures is green-always. The gate MUST judge a fixture the validator rejects.");
                return 1;
            }
            if (mismatches.Count > 0)
            {
                Debug.LogError($"[validate-fixtures] FAIL: {mismatches.Count} verdict mismatch(es).");
                return 1;
            }

            Debug.Log($"[validate-fixtures] PASS: {asserted} verdict(s) matched ({reds} red).");
            return 0;
        }

        private static Dictionary<string, Expectation> BuildExpectations(ExpectFileDto expect)
        {
            var map = new Dictionary<string, Expectation>(StringComparer.Ordinal);
            if (expect?.Levels == null)
                return map;
            foreach (var lvl in expect.Levels)
            {
                if (lvl == null || string.IsNullOrEmpty(lvl.LevelId) || string.IsNullOrWhiteSpace(lvl.ExpectValidator))
                    continue;
                var verdict = lvl.ExpectValidator.Trim().ToLowerInvariant();
                if (verdict != "accept" && verdict != "reject")
                    continue;
                map[lvl.LevelId] = new Expectation { Verdict = verdict, ErrorSubstring = lvl.ExpectError };
            }
            return map;
        }

        private struct Expectation
        {
            public string Verdict;        // "accept" | "reject"
            public string ErrorSubstring; // optional, reject only
        }

        /// <summary>Parallel-to-<see cref="CampaignFileDto"/> shape that reads ONLY the TASK-332 expectation fields, so
        /// the same fixture file deserialises once as domain (via <see cref="CampaignFileDto"/>) and once as
        /// expectations. Newtonsoft ignores the domain fields here and the expectation fields there.</summary>
        private sealed class ExpectFileDto
        {
            [JsonProperty("levels")] public List<ExpectLevelDto> Levels { get; set; }
        }

        private sealed class ExpectLevelDto
        {
            [JsonProperty("levelId")] public string LevelId { get; set; }
            [JsonProperty("expectValidator")] public string ExpectValidator { get; set; }
            [JsonProperty("expectError")] public string ExpectError { get; set; }
        }
    }
}
#endif
