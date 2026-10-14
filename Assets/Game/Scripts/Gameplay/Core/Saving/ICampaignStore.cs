namespace Gameplay.Core
{
    /// <summary>
    /// Persistence seam for the campaign meta-state (TASK-044; TASK-528 widened it from a bare index to
    /// <see cref="CampaignProgress"/>). The domain stays Data/PlayerPrefs-clean while the concrete store (Unity-side
    /// adapter) maps it onto the SDK-canon 1-based <c>Meta.CurrentLevel</c> plus the additive
    /// <c>Meta.LineupCleared</c>/<c>Meta.PoolCursor</c> keys through the Say wrapper (prod: <c>MetaCampaignStore</c>;
    /// bringup: <c>PrefsCampaignStore</c>). A bare index is gone on purpose: after the line-up the shown number and the
    /// loaded level diverge, and an index without its track means nothing. Only these numbers are persisted — a kill
    /// mid-level reopens the level from scratch (there is no in-level snapshot; the retired write-only contour was BUG-2).
    /// </summary>
    public interface ICampaignStore
    {
        /// <summary>The saved progress; <see cref="CampaignProgress.Fresh"/> on a fresh install (missing keys default).
        /// Interpretation and clamping are <see cref="CampaignCursor"/>'s job.</summary>
        CampaignProgress Read();

        /// <summary>Persist the progress and flush it durably to disk (a WIN checkpoint that survives a kill).</summary>
        void Write(CampaignProgress progress);
    }
}
