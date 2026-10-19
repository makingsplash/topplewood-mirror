namespace Gameplay.Core
{
    /// <summary>
    /// One piece of the won figure (TASK-634): which placement filled which target slot, the physical rest pose it
    /// was judged at, and the angle it lands on in the target card — the slot angle NEAREST the rest pose under the
    /// piece's rotational symmetry (<c>Rest − SignedSymmetryDelta(Rest − slot, order)</c>), so the flight never adds
    /// a spurious turn. Built by the mechanic controller from the judge's own pairs; transient and observe-only — never
    /// persisted, never read by the sim graph.
    /// </summary>
    public readonly struct WinFigurePiece
    {
        public readonly int PlacementIndex;
        public readonly int SlotIndex;
        public readonly Vec2 RestPosition;
        public readonly float RestRotation;
        public readonly float LandRotation;

        public WinFigurePiece(int placementIndex, int slotIndex, Vec2 restPosition, float restRotation, float landRotation)
        {
            PlacementIndex = placementIndex;
            SlotIndex = slotIndex;
            RestPosition = restPosition;
            RestRotation = restRotation;
            LandRotation = landRotation;
        }
    }
}
