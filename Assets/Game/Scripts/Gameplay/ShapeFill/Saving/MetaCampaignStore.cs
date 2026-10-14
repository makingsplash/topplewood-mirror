using DataStorage;
using Gameplay.Core;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Production <see cref="ICampaignStore"/> (TASK-044, widened by TASK-528): the shown level number IS the SDK-canon
    /// <c>Data.Meta.CurrentLevel</c> (1-based <c>PlayerPrefsInt "Meta.CurrentLevel"</c>) — the very field
    /// <c>LevelText</c> and <c>RewardedStateService</c> already read, so the UI level number and the monetisation
    /// pacing stay coherent with real progress (fixes the BUG-3 two-sources split). Writes go to the SHARED
    /// <c>Data.Meta.CurrentLevel</c> INSTANCE (never a second <c>PlayerPrefsInt</c> on the same key) so those
    /// readers see the fresh value without a <c>Refresh</c> (RFC risk R1). Since TASK-528 the number keeps growing past
    /// the line-up, so it is no longer a line-up position: <c>Meta.LineupCleared</c>/<c>Meta.PoolCursor</c> carry the
    /// track, and <see cref="CampaignCursor"/> turns the three into a level. <see cref="Write"/> flushes to disk
    /// immediately — a durable WIN checkpoint that survives a kill without a normal exit (RFC §8).
    /// </summary>
    public sealed class MetaCampaignStore : ICampaignStore
    {
        public CampaignProgress Read()
        {
            return new CampaignProgress(
                Data.Meta.CurrentLevel.Value, Data.Meta.LineupCleared.Value, Data.Meta.PoolCursor.Value);
        }

        public void Write(CampaignProgress progress)
        {
            Data.Meta.CurrentLevel.Value = progress.LevelNumber;            // 1-based SDK canon (UI + monetisation read this)
            Data.Meta.CurrentLevelIndex.Value = progress.LevelNumber - 1;   // keep the 0-based SDK mirror coherent
            Data.Meta.LineupCleared.Value = progress.LineupCleared;
            Data.Meta.PoolCursor.Value = progress.PoolCursor;
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();             // durable WIN checkpoint (RFC §8)
        }
    }
}
