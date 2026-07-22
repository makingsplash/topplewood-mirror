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
        void Dispose();
    }
}
