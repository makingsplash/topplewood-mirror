namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Minimal seam the Restart button uses to restart the current level (TASK-116, RFC §2.4). Implemented by
    /// <see cref="Gameplay.Adapters.CampaignProgressionController"/> so restart goes through the *existing*
    /// index owner (reset-in-place reload) — never a second campaign index. Restart is deliberately neutral for
    /// the domain (Q3): it reloads the current level without going through <c>Resolve(Lost)</c>, so it is not a
    /// loss/attempt and does not touch the persisted pointer.
    /// </summary>
    public interface ICampaignRestart
    {
        void ReloadCurrent();
    }
}
