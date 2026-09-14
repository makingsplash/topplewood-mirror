namespace Gameplay.Core
{
    /// <summary>
    /// Mechanic seam that keeps the framework mechanic-agnostic (overview §Швы механики). TASK-004
    /// swaps NullMechanicController for the shape_fill controller. Level context is read from GameModel,
    /// so the seam stays generic (Tick + OnIntent only).
    /// </summary>
    public interface IMechanicController
    {
        void Tick(float dt);
        void OnIntent(InputIntent intent);

        /// <summary>TASK-237 / TASK-374: true while a co-release group is mid-gesture (a member has been tapped and its
        /// coincidence window is still open, but the group has not yet completed). The Hint booster reads this to avoid
        /// spending a charge on a piece already being co-released — it treats an open window as a neutral no-op. Always
        /// false for pieces not in a co-release group (they release immediately on tap).</summary>
        bool HasPendingRelease { get; }
    }
}
