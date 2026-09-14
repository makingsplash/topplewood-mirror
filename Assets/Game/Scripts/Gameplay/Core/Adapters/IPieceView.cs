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

        // TASK-374: the SetArmed seam was removed with the balance coalescence helper. Co-release is a CONDITION now,
        // not a helper: a tapped-but-not-released group member shows NOTHING (no "armed" telegraph), so no view ever
        // needs to be armed. A piece is either Idle (dim) or released (Active) — see SetState.

        void Dispose();
    }
}
