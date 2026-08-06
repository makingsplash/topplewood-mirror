using Gameplay.Core;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Bringup <see cref="ICampaignStore"/> (TASK-044): the isolated gray-box scene deliberately does NOT raise
    /// <c>Data</c>/<c>SayData</c> (RFC R5), so it cannot go through <see cref="MetaCampaignStore"/>. It owns its
    /// own <see cref="PlayerPrefsInt"/> on the SAME real key <c>Meta.CurrentLevel</c> — a shared backend with
    /// production, so progress survives Stop→Play and (living in the editor prefs plist, not
    /// <c>persistentDataPath</c>) is untouched by deleting save files. Requires only <c>PlayerPrefsCollection.Init</c>
    /// (done first in the bringup scope, RFC §3), never the full Data bootstrap. Bringup renders no level number
    /// and no monetisation, so a second <c>PlayerPrefsInt</c> instance on the key is harmless (no coherence reader).
    /// </summary>
    public sealed class PrefsCampaignStore : ICampaignStore
    {
        private readonly PlayerPrefsInt _currentLevel = new("Meta.CurrentLevel", 1);

        public int ReadLevelIndex()
        {
            return _currentLevel.Value - 1;
        }

        public void WriteLevelIndex(int levelIndex)
        {
            _currentLevel.Value = levelIndex + 1;                 // 1-based, same key as production
            _currentLevel.ForceSave();                            // TASK-109 M-2: durable even when value==RealtimeValue
                                                                  // (ShouldSave gate suppresses the setter, PlayerPrefsValue.cs:43-46)
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();  // durable WIN checkpoint (RFC §8)
        }
    }
}
