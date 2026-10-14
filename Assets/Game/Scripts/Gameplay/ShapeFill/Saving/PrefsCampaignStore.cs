using Gameplay.Core;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Bringup <see cref="ICampaignStore"/> (TASK-044, widened by TASK-528): the isolated gray-box scene deliberately
    /// does NOT raise <c>Data</c>/<c>SayData</c> (RFC R5), so it cannot go through <see cref="MetaCampaignStore"/>. It
    /// owns its own <see cref="PlayerPrefsInt"/> instances on the SAME real keys (<c>Meta.CurrentLevel</c>,
    /// <c>Meta.LineupCleared</c>, <c>Meta.PoolCursor</c>) — a shared backend with production, so progress survives
    /// Stop→Play and (living in the editor prefs plist, not <c>persistentDataPath</c>) is untouched by deleting save
    /// files. Requires only <c>PlayerPrefsCollection.Init</c> (done first in the bringup scope, RFC §3), never the full
    /// Data bootstrap. Bringup renders no level number and no monetisation, so second instances on the keys are
    /// harmless (no coherence reader).
    /// </summary>
    public sealed class PrefsCampaignStore : ICampaignStore
    {
        private readonly PlayerPrefsInt _currentLevel = new("Meta.CurrentLevel", 1);
        private readonly PlayerPrefsInt _lineupCleared = new("Meta.LineupCleared", 0);
        private readonly PlayerPrefsInt _poolCursor = new("Meta.PoolCursor", 0);

        public CampaignProgress Read()
        {
            return new CampaignProgress(_currentLevel.Value, _lineupCleared.Value, _poolCursor.Value);
        }

        public void Write(CampaignProgress progress)
        {
            _currentLevel.Value = progress.LevelNumber;           // 1-based, same key as production
            _lineupCleared.Value = progress.LineupCleared;
            _poolCursor.Value = progress.PoolCursor;
            // TASK-109 M-2: durable even when value==RealtimeValue (ShouldSave gate suppresses the setter,
            // PlayerPrefsValue.cs:43-46) — for every key, or a reset to a default could stay unwritten.
            _currentLevel.ForceSave();
            _lineupCleared.ForceSave();
            _poolCursor.ForceSave();
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();  // durable WIN checkpoint (RFC §8)
        }
    }
}
