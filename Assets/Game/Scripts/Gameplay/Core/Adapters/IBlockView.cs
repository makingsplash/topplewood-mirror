namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// View seam for a single block — the domain never sees a MonoBehaviour. Unity impl syncs a mesh
    /// from the mirrored pose each frame and can react to life-cycle changes (juice on settle).
    /// </summary>
    public interface IBlockView
    {
        void Sync(Vec2 position, float rotation);
        void SetState(BlockLifecycle state);
        void Dispose();
    }
}
