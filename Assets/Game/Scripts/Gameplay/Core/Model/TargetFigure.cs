using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// The picture-to-build (GAME_RULES §1): a union <see cref="Outline"/> (for the ghost overlay + optional
    /// overlap sanity-gate) plus the per-piece target <see cref="Slots"/> judged by pose-match (RFC §4).
    /// Replaces the removed coverage-model Silhouette.
    /// </summary>
    public sealed class TargetFigure
    {
        public Polygon Outline { get; }
        public IReadOnlyList<TargetSlot> Slots { get; }

        public TargetFigure(Polygon outline, IReadOnlyList<TargetSlot> slots)
        {
            Outline = outline;
            Slots = slots;
        }
    }
}
