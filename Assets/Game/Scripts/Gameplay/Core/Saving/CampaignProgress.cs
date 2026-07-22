using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// The campaign meta-pointer (TASK-009): which level is open + whether the campaign is finished. A tiny
    /// POCO with its OWN <c>schemaVersion</c>, persisted SEPARATELY from the in-level <see cref="GameSnapshot"/>
    /// (dev-architect 17:25 §5). Per the human (14:23) only the level number is saved; in-level progress is
    /// NOT — so a kill mid-level reopens the level from scratch. Foreign/older schema → fresh (index 0).
    /// </summary>
    public sealed class CampaignProgress
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty("campaignIndex")] public int CampaignIndex { get; set; }
        [JsonProperty("campaignCompleted")] public bool CampaignCompleted { get; set; }
    }
}
