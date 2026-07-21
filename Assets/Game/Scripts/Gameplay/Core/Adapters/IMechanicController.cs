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
    }
}
