using Gameplay.Core;

namespace Gameplay.View
{
    /// <summary>
    /// Holds the current <see cref="TargetCardPose"/> and the target it shows (TASK-613 plan §3.5). Written by
    /// <see cref="TargetPreviewPresenter"/> on every rebuild and invalidated on clear; read by the tour view each frame,
    /// so the start order of the two entry points does not matter. Core singleton, context-independent.
    /// </summary>
    public sealed class TargetCardLocator
    {
        public TargetCardPose Current { get; private set; }

        /// <summary>The target drawn on the card (its slots, in target space), or null when there is no card.</summary>
        public TargetFigure Target { get; private set; }

        public void Set(TargetCardPose pose, TargetFigure target)
        {
            Current = pose;
            Target = target;
        }

        public void Invalidate()
        {
            Current = default;
            Target = null;
        }
    }
}
