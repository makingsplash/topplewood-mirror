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

        /// <summary>Telegraph that this Idle piece is armed and waiting for a deferred release — either the
        /// balance-mode coalescence group (releases synchronously with its pair on flush, TASK-023 §2.4) or the
        /// assembly/catapult serialized release queue (releases one at a time on field-ready, TASK-061). Cosmetic
        /// only: the model stays Idle until release; headless views (sim) are no-ops.</summary>
        void SetArmed(bool armed);

        void Dispose();
    }
}
