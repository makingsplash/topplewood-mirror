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

        /// <summary>TASK-237: true while a balance coalescence window is arming (a piece has been tapped but not yet
        /// flushed). The Hint booster reads this to avoid spending a charge on an already-armed piece — it treats an
        /// open window as a neutral no-op. Always false for the single-release assembly path.</summary>
        bool HasPendingRelease { get; }
    }
}
