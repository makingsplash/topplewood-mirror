using Services.Gameplay;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// The seam through which <see cref="GatedResolutionFlow"/> offers an ad in the pause between levels (TASK-658).
    /// Fire-and-forget by contract: the caller runs its transition right after, it never waits for the ad to close
    /// (in the template SDK wrapper's Dev ad mode the close callback never arrives — plan §3.4). Prod binds
    /// <c>Gameplay.Adapters.SayKitBetweenLevelsAds</c>; the threshold, the SDK gates and the logging live there.
    /// </summary>
    public interface IBetweenLevelsAds
    {
        void TryShow(BetweenLevelsMoment moment);
    }
}
