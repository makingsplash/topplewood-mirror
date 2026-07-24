namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// View seam for a single piece — the domain never sees a MonoBehaviour. Unity impl syncs a mesh from
    /// the mirrored pose each frame and reacts to life-cycle changes (dim while Idle, full once released).
    /// </summary>
    public interface IPieceView
    {
        void Sync(Vec2 position, float rotation);
        void SetState(PieceLifecycle state);

        /// <summary>Telegraph that this Idle piece is armed and waiting in the balance-mode coalescence group —
        /// it will release synchronously with its pair when the group flushes (TASK-023 §2.4). Cosmetic only:
        /// the model stays Idle until the flush; headless views (sim) are no-ops.</summary>
        void SetArmed(bool armed);

        void Dispose();
    }
}
