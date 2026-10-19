using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Holds the current <see cref="TargetCardPose"/>, the target it shows and the card's root transform (TASK-613 plan
    /// §3.5; root since TASK-634). Written by <see cref="TargetPreviewPresenter"/> on every rebuild and invalidated on
    /// clear; read by the tour view each frame and by the win beat on Won, so the start order of the entry points does
    /// not matter. The win beat parents the landed figure under <see cref="CardRoot"/> and punches its scale — the root
    /// is destroyed with the card on the next rebuild, the figure with it. Core singleton, context-independent.
    /// </summary>
    public sealed class TargetCardLocator
    {
        public TargetCardPose Current { get; private set; }

        /// <summary>The target drawn on the card (its slots, in target space), or null when there is no card.</summary>
        public TargetFigure Target { get; private set; }

        /// <summary>The card's root (the transform <see cref="TargetCardPose"/> was read from), or null.</summary>
        public Transform CardRoot { get; private set; }

        public void Set(TargetCardPose pose, TargetFigure target) => Set(pose, target, null);

        public void Set(TargetCardPose pose, TargetFigure target, Transform cardRoot)
        {
            Current = pose;
            Target = target;
            CardRoot = cardRoot;
        }

        public void Invalidate()
        {
            Current = default;
            Target = null;
            CardRoot = null;
        }
    }
}
