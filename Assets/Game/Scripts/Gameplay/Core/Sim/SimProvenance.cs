using System;
using System.Diagnostics;
using System.Globalization;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Git provenance of a sim sweep (TASK-058): which tree the report was actually snapped from, whether that
    /// tree carried uncommitted edits, and when the sweep started. Written into the report's <c>generator</c>
    /// block so acceptance reads the ref/dirty straight from the artifact instead of reconstructing it by
    /// correlating the file's timestamp against the commit log (the TASK-050 incident). Unity-free: git is
    /// invoked via <see cref="Process"/> with the worktree as CWD, so a headless sweep in the sim-worktree
    /// captures THAT worktree's detached ref (including the synthetic snapshot commit run-sim.sh mints for a
    /// dirty main tree — clean checkout of it ⇒ <see cref="Dirty"/> false, which is the honest reading).
    /// </summary>
    public sealed class SimProvenance
    {
        public string Commit { get; }    // full sha of the checked-out ref, or "unknown" if git is unavailable
        public string Branch { get; }    // abbrev ref; "HEAD" when detached (the norm for the sim-worktree)
        public bool? Dirty { get; }      // uncommitted edits present in the source tree; null = undeterminable
        public string Worktree { get; }  // absolute path of the tree the sweep ran from (solves sim-wt vs main)
        public string StartedAt { get; } // ISO-8601 round-trip local time the sweep started

        public SimProvenance(string commit, string branch, bool? dirty, string worktree, string startedAt)
        {
            Commit = commit;
            Branch = branch;
            Dirty = dirty;
            Worktree = worktree;
            StartedAt = startedAt;
        }

        /// <summary>
        /// Captures provenance by shelling out to git in <paramref name="worktreePath"/>. Never throws: on any
        /// failure (git missing, path not a repo — e.g. a player build under persistentDataPath) the git-derived
        /// fields degrade to "unknown" / null so the report still writes. <see cref="Dirty"/> is a nullable bool
        /// on purpose — null ("could not determine") is machine-distinguishable from false ("clean").
        /// </summary>
        public static SimProvenance Capture(string worktreePath, DateTime startedAt)
        {
            var startedIso = startedAt.ToString("o", CultureInfo.InvariantCulture);
            var commit = RunGit(worktreePath, "rev-parse HEAD") ?? "unknown";
            var branch = RunGit(worktreePath, "rev-parse --abbrev-ref HEAD") ?? "unknown";
            // Scope the dirty check to the tracked SOURCE set — mirrors run-sim.sh's own dirty detection. A plain
            // `status --porcelain` here reports dirty on Unity/harness-generated untracked files (the freshly
            // written report under Artifacts/, sim-run.log, Library/ — none gitignored), which would make every
            // sim-worktree run read dirty despite being a clean detached checkout. These four dirs are exactly
            // what the sweep consumes and what run-sim.sh snapshots, so this is the honest "source edited?" signal.
            var status = RunGit(worktreePath, "status --porcelain -- Assets Packages ProjectSettings docs");
            bool? dirty = status == null ? (bool?)null : status.Length > 0;
            return new SimProvenance(commit, branch, dirty, worktreePath, startedIso);
        }

        // Trimmed stdout of `git <args>` run in worktreePath, or null if git could not run or exited non-zero.
        private static string RunGit(string worktreePath, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    WorkingDirectory = worktreePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return null;

                var stdout = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd(); // drain so the child never blocks on a full stderr pipe
                process.WaitForExit();
                return process.ExitCode == 0 ? stdout.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
