namespace Gameplay.Core
{
    /// <summary>
    /// Persistence seam for the campaign meta-pointer (TASK-009, dev-architect 17:25 §5). Deliberately
    /// SEPARATE from <see cref="IShapeFillSaveStore"/> (the in-level snapshot sink) so the two concerns do not
    /// share a VContainer binding. The concrete store (file / cloud) is a Unity-side adapter.
    /// </summary>
    public interface ICampaignStore
    {
        void Write(CampaignProgress progress);

        /// <summary>The stored progress, or null when there is no save or the schema is foreign (→ fresh).</summary>
        CampaignProgress Read();
    }
}
