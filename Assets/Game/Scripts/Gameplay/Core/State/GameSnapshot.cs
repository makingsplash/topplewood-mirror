using System.Collections.Generic;
using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// shape_fill save body (RFC §8). Schema v3 (TASK-007 pivot: pre-placed pieces, no pool cursor).
    /// Newtonsoft with stable field names. Snapshot is taken only at rest (no Falling pieces) — mid-fall
    /// velocity restore is intentionally not persisted. Older/foreign schema (v1/v2) → start the level fresh.
    /// </summary>
    public sealed class GameSnapshot : IGameSnapshot
    {
        public const int CurrentSchemaVersion = 3;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty("levelId")] public string LevelId { get; set; }
        [JsonProperty("phase")] public GamePhase Phase { get; set; }
        [JsonProperty("attemptsUsed")] public int AttemptsUsed { get; set; }
        [JsonProperty("pieces")] public List<PieceSnapshot> Pieces { get; set; } = new();
    }

    /// <summary>Per-piece persisted state. Body id / rest timer are transient and not saved.</summary>
    public sealed class PieceSnapshot
    {
        [JsonProperty("instanceId")] public int InstanceId { get; set; }
        [JsonProperty("placementIndex")] public int PlacementIndex { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("state")] public PieceLifecycle State { get; set; }
        [JsonProperty("posX")] public float PosX { get; set; }
        [JsonProperty("posY")] public float PosY { get; set; }
        [JsonProperty("rot")] public float Rot { get; set; }
    }
}
