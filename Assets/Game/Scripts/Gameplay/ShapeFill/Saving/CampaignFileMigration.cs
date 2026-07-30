using System.IO;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// One-time file→prefs migration for the campaign pointer (TASK-044 §5). Gameplay NEVER incremented
    /// <c>Meta.CurrentLevel</c> before this task (only the debug menu did), so an existing player has their real
    /// progress in the legacy JSON file while the pref still sits at default 1. This imports the legacy index
    /// into <paramref name="currentLevel"/>, then deletes the legacy files. Gated by
    /// <paramref name="migratedMarker"/> so it runs exactly once per device: whichever store reads first (prod on
    /// device OR bringup in the editor) migrates, the other sees the marker and skips. Unity-free apart from
    /// <c>Application.persistentDataPath</c>/<c>File</c> so QA can exercise it in isolation.
    /// </summary>
    public static class CampaignFileMigration
    {
        private const string CampaignFile = "shapefill_campaign_progress.json";
        private const string CampaignBackupFile = "shapefill_campaign_progress.json.bak";
        private const string InLevelSaveFile = "shapefill_save.json";

        public static void RunIfNeeded(PlayerPrefsInt currentLevel, PlayerPrefsBool migratedMarker)
        {
            if (migratedMarker.Value)
                return;

            var dir = Application.persistentDataPath;
            var campaignPath = Path.Combine(dir, CampaignFile);

            if (File.Exists(campaignPath))
            {
                // Import ONLY the level index; the legacy campaignCompleted flag is deliberately ignored so the
                // stuck "index=17 + completed=true" state from BUG-1 heals (the controller clamps on read).
                var index = TryReadLegacyIndex(campaignPath);
                if (index.HasValue)
                    currentLevel.Value = index.Value + 1; // 0-based file index → 1-based pref
            }

            // The index is now imported into the pref, so the migration has effectively happened. Mark it done and
            // flush BEFORE the destructive cleanup below: legacy-file removal is best-effort hygiene, and even a
            // total failure there must not re-arm the migration on the next boot (a re-run would re-import a stale
            // index over live progress → BUG-1 redux + boot-loop while the file lingers).
            migratedMarker.Value = true;
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();

            // Purge every legacy persistentDataPath save file: the campaign pointer now lives in prefs and the
            // in-level snapshot is retired dead code (BUG-2), so neither file has a reader anymore.
            DeleteIfExists(campaignPath);
            DeleteIfExists(Path.Combine(dir, CampaignBackupFile));
            DeleteIfExists(Path.Combine(dir, InLevelSaveFile));
        }

        private static int? TryReadLegacyIndex(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                    return null;
                var dto = JsonConvert.DeserializeObject<LegacyCampaignProgress>(json);
                return dto?.CampaignIndex;
            }
            catch
            {
                return null; // unreadable/corrupt legacy file → fresh start (default pref stays)
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup (mirrors the guarded read path): a locked / permission-denied legacy file must
                // not crash migration. The marker is already set + flushed above, so the file just lingers harmlessly.
            }
        }

        /// <summary>Minimal shape of the retired <c>CampaignProgress</c> JSON — only the index is migrated.</summary>
        private sealed class LegacyCampaignProgress
        {
            [JsonProperty("campaignIndex")] public int CampaignIndex { get; set; }
        }
    }
}
