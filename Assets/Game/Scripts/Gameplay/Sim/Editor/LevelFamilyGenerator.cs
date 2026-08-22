#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Gameplay.Sim.Editor
{
    /// <summary>
    /// TASK-185 — Unity editor BATCH entry for the level-family generator. This is the thin engine/I-O shell around the
    /// Unity-free core <see cref="FamilyExpander"/>: it reads env args, loads the skeleton + campaign files, delegates
    /// the real expansion/envelope/emit work to the core, writes the mutated manifest + a JSON report, and exits with a
    /// status. Emitting DATA (ordinary <see cref="LevelDataDto"/> records the runtime and the sim gate both read) — not
    /// a runtime source — is the RFC decision (TASK-180/02 §2), because the sim gate judges a git snapshot of the file.
    ///
    /// Invoked (mirrors <see cref="LevelSimBatch"/>) by <c>.studio/tools/gen-levels.sh</c>:
    /// <code>&lt;Unity&gt; -batchmode -projectPath &lt;repo&gt; -executeMethod Gameplay.Sim.Editor.LevelFamilyGenerator.Gen</code>
    /// Env in: GEN_SKELETON (REQUIRED — path to skeleton json; refuse rather than default, per CLAUDE.md portability),
    /// GEN_MANIFEST (default StreamingAssets campaign file), GEN_REPORT (default Artifacts/level-gen/gen-report.json),
    /// GEN_DRYRUN (non-empty → validate + report, do NOT write the manifest). Exit: 0 = ≥1 level emitted, 1 = zero
    /// emitted or crash, 2 = bad args/config.
    /// </summary>
    public static class LevelFamilyGenerator
    {
        public static void Gen()
        {
            var code = 1;
            try { code = Run(); }
            catch (Exception e) { Debug.LogError($"[level-gen] FAILED: {e.Message}\n{e.StackTrace}"); code = 1; }
            EditorApplication.Exit(code);
        }

        private static int Run()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

            var skeletonPath = Environment.GetEnvironmentVariable("GEN_SKELETON");
            if (string.IsNullOrWhiteSpace(skeletonPath))
            {
                Debug.LogError("[level-gen] GEN_SKELETON is required (path to a skeleton definition json) — refusing " +
                               "rather than defaulting to this game's skeletons. See .studio/tools/gen-levels.sh.");
                return 2;
            }
            skeletonPath = Rooted(skeletonPath, projectRoot);
            if (!File.Exists(skeletonPath)) { Debug.LogError($"[level-gen] skeleton not found: {skeletonPath}"); return 2; }

            var manifestPath = Environment.GetEnvironmentVariable("GEN_MANIFEST");
            manifestPath = string.IsNullOrWhiteSpace(manifestPath)
                ? Path.Combine(Application.dataPath, "StreamingAssets", JsonLevelSource.CampaignFileName)
                : Rooted(manifestPath, projectRoot);
            if (!File.Exists(manifestPath)) { Debug.LogError($"[level-gen] manifest not found: {manifestPath}"); return 2; }

            var reportPath = Environment.GetEnvironmentVariable("GEN_REPORT");
            reportPath = string.IsNullOrWhiteSpace(reportPath)
                ? Path.Combine(projectRoot, "Artifacts", "level-gen", "gen-report.json")
                : Rooted(reportPath, projectRoot);

            var dryRun = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEN_DRYRUN"));

            var skeleton = JsonConvert.DeserializeObject<SkeletonFileDto>(File.ReadAllText(skeletonPath));
            var manifestText = File.ReadAllText(manifestPath);

            var result = new FamilyExpander().Expand(skeleton, manifestText);
            foreach (var line in result.Log) Debug.Log($"[level-gen] {line}");
            if (!result.Ok) { Debug.LogError($"[level-gen] {result.Error}"); return 2; }

            var appended = false;
            if (result.AppendedManifest != null && !dryRun)
            {
                File.WriteAllText(manifestPath, result.AppendedManifest);
                appended = true;
                Debug.Log($"[level-gen] appended {result.EmittedIds.Count} level(s) to {manifestPath}");
            }

            WriteReport(reportPath, skeleton, manifestPath, result, appended, dryRun);
            Debug.Log($"[level-gen] DONE: emitted={result.EmittedIds.Count} rejected={result.Rejected.Count} " +
                      $"appended={appended} dryRun={dryRun}");
            Debug.Log($"[level-gen] emitted ids: {string.Join(",", result.EmittedIds)}");
            return result.EmittedIds.Count > 0 ? 0 : 1;
        }

        private static void WriteReport(string reportPath, SkeletonFileDto sk, string manifestPath,
            FamilyExpander.Result result, bool appended, bool dryRun)
        {
            var dir = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var report = new
            {
                skeleton = sk.Skeleton,
                family = sk.Family,
                manifest = manifestPath,
                appended,
                dryRun,
                emitted = result.EmittedIds,
                rejected = result.Rejected.Select(x => new { id = x.Id, reason = x.Reason }).ToList(),
            };
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            Debug.Log($"[level-gen] report → {reportPath}");
        }

        private static string Rooted(string path, string projectRoot)
            => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectRoot, path));
    }
}
#endif
