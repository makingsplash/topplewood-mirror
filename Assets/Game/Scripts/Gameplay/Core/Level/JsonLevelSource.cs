using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// JSON-backed <see cref="ILevelSource"/> (TASK-009): the campaign's ordered levels, authored as data in
    /// <c>shapefill_levels.json</c> and read through the Unity-free <see cref="ILevelStore"/> seam. Replaces
    /// the hardcoded <c>ShapeFillLevelSource</c>/<c>EmbeddedTestLevels</c> path for the shipped game (the
    /// human's 15:04 "storage system" mandate). Levels are parsed + structurally validated eagerly at
    /// construction (fail-fast, RFC §7.3 via <see cref="LevelDataValidator"/>); <see cref="Load"/> clamps the
    /// index so a stale campaign pointer never throws.
    /// </summary>
    public sealed class JsonLevelSource : ILevelSource
    {
        public const string CampaignFileName = "shapefill_levels.json";

        private readonly List<LevelData> _levels;

        public JsonLevelSource(ILevelStore store, LevelDataValidator validator)
        {
            var json = store.ReadText(CampaignFileName);
            if (string.IsNullOrEmpty(json))
                throw new InvalidOperationException(
                    $"JsonLevelSource: level content '{CampaignFileName}' is missing or empty.");

            var file = JsonConvert.DeserializeObject<CampaignFileDto>(json);
            if (file?.Levels == null || file.Levels.Count == 0)
                throw new InvalidOperationException(
                    $"JsonLevelSource: '{CampaignFileName}' contains no levels.");

            _levels = new List<LevelData>(file.Levels.Count);
            var errors = new StringBuilder();
            for (var i = 0; i < file.Levels.Count; i++)
            {
                var level = file.Levels[i].ToDomain();
                var result = validator.Validate(level);
                if (!result.Ok)
                    errors.Append($"\n  [{i}] '{level.LevelId}': {string.Join("; ", result.Errors)}");
                _levels.Add(level);
            }

            if (errors.Length > 0)
                throw new InvalidOperationException(
                    $"JsonLevelSource: '{CampaignFileName}' has invalid levels:{errors}");
        }

        public int Count => _levels.Count;

        public LevelData Load(int index)
        {
            if (index < 0)
                index = 0;
            else if (index >= _levels.Count)
                index = _levels.Count - 1;
            return _levels[index];
        }
    }
}
