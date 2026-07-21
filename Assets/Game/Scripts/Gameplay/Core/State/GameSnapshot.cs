using System.Collections.Generic;
using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// shape_fill save body (RFC §6). Schema v2. Newtonsoft with stable field names. Snapshot is taken
    /// only at rest (no Falling blocks) — mid-fall velocity restore is intentionally not persisted.
    /// v1 had no domain state (stub); migration = start the level fresh.
    /// </summary>
    public sealed class GameSnapshot : IGameSnapshot
    {
        public const int CurrentSchemaVersion = 2;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty("levelId")] public string LevelId { get; set; }
        [JsonProperty("poolCursor")] public int PoolCursor { get; set; }
        [JsonProperty("phase")] public GamePhase Phase { get; set; }
        [JsonProperty("attemptsUsed")] public int AttemptsUsed { get; set; }
        [JsonProperty("blocks")] public List<BlockSnapshot> Blocks { get; set; } = new();
    }

    /// <summary>Per-block persisted state. Body id / rest timer are transient and not saved.</summary>
    public sealed class BlockSnapshot
    {
        [JsonProperty("instanceId")] public int InstanceId { get; set; }
        [JsonProperty("poolIndex")] public int PoolIndex { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("state")] public BlockLifecycle State { get; set; }
        [JsonProperty("posX")] public float PosX { get; set; }
        [JsonProperty("posY")] public float PosY { get; set; }
        [JsonProperty("rot")] public float Rot { get; set; }
    }
}
