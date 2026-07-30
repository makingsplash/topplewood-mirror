namespace Gameplay.Core
{
    /// <summary>
    /// Persistence seam for the campaign meta-pointer (TASK-044). A single 0-based level index — the domain
    /// stays Data/PlayerPrefs-clean while the concrete store (Unity-side adapter) maps it onto the SDK-canon
    /// 1-based <c>Meta.CurrentLevel</c> pref through the Say wrapper (prod: <c>MetaCampaignStore</c>; bringup:
    /// <c>PrefsCampaignStore</c>). Only the level number is persisted — a kill mid-level reopens the level from
    /// scratch (there is no in-level snapshot anymore; the retired write-only contour was BUG-2).
    /// </summary>
    public interface ICampaignStore
    {
        /// <summary>0-based index of the saved level; 0 on a fresh install. Clamping is the controller's job.</summary>
        int ReadLevelIndex();

        /// <summary>Persist the pointer and flush it durably to disk (a WIN checkpoint that survives a kill).</summary>
        void WriteLevelIndex(int levelIndex);
    }
}
