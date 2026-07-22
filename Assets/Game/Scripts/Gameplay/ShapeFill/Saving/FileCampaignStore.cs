using System.IO;
using Gameplay.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// File-backed <see cref="ICampaignStore"/> (TASK-009): the campaign meta-pointer as a single JSON file
    /// under <c>Application.persistentDataPath</c>, separate from the in-level save file. Self-contained (no
    /// DataStorage/PlayerPrefs) so it works in the isolated bringup scope and production alike (mirrors
    /// <see cref="FileShapeFillSaveStore"/>). Foreign/older schema or a read error → null (caller starts fresh).
    /// </summary>
    public sealed class FileCampaignStore : ICampaignStore
    {
        private const string FileName = "shapefill_campaign_progress.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public void Write(CampaignProgress progress) => File.WriteAllText(FilePath, JsonConvert.SerializeObject(progress));

        public CampaignProgress Read()
        {
            var path = FilePath;
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrEmpty(json))
                return null;

            var progress = JsonConvert.DeserializeObject<CampaignProgress>(json);
            if (progress == null || progress.SchemaVersion != CampaignProgress.CurrentSchemaVersion)
                return null; // foreign / older schema → fresh, mirroring the GameSnapshot v3 policy.

            return progress;
        }
    }
}
