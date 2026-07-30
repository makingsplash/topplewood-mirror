using DataStorage;
using Gameplay.Core;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Production <see cref="ICampaignStore"/> (TASK-044): the campaign pointer IS the SDK-canon
    /// <c>Data.Meta.CurrentLevel</c> (1-based <c>PlayerPrefsInt "Meta.CurrentLevel"</c>) — the very field
    /// <c>LevelText</c> and <c>RewardedStateService</c> already read, so the UI level number and the monetisation
    /// pacing stay coherent with real progress (fixes the BUG-3 two-sources split). Writes go to the SHARED
    /// <c>Data.Meta.CurrentLevel</c> INSTANCE (never a second <c>PlayerPrefsInt</c> on the same key) so those
    /// readers see the fresh value without a <c>Refresh</c> (RFC risk R1). 0-based campaign index maps as
    /// <c>index = CurrentLevel.Value - 1</c>. <see cref="WriteLevelIndex"/> flushes to disk immediately — a
    /// durable WIN checkpoint that survives a kill without a normal exit (RFC §8). The one-time file→prefs
    /// migration runs on first read (RFC §5).
    /// </summary>
    public sealed class MetaCampaignStore : ICampaignStore
    {
        public int ReadLevelIndex()
        {
            CampaignFileMigration.RunIfNeeded(Data.Meta.CurrentLevel, Data.Meta.MigratedCampaignFileV1);
            return Data.Meta.CurrentLevel.Value - 1;
        }

        public void WriteLevelIndex(int levelIndex)
        {
            Data.Meta.CurrentLevel.Value = levelIndex + 1;       // 1-based SDK canon (UI + monetisation read this)
            Data.Meta.CurrentLevelIndex.Value = levelIndex;      // keep the previously-dead 0-based SDK mirror coherent
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();  // durable WIN checkpoint (RFC §8)
        }
    }
}
