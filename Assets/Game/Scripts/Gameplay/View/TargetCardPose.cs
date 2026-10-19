using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Where the target card sits in the world (TASK-613 plan §3.5, T2): ONE affine map "target space → world" plus the
    /// card's outer frame. The target is authored in the field's world coordinates (<c>PoseMatchEvaluator</c> compares
    /// slot and piece positions directly), and the card draws it shifted by the figure centre and uniformly scaled
    /// (<see cref="TargetPreviewView"/>), so a slot, the card cut-out and the win beat's landing (TASK-634; the TASK-613 ✓
    /// anchor and "ghost" start used it too) are all derived from this one pose — no second set of numbers to drift.
    /// </summary>
    public readonly struct TargetCardPose
    {
        public readonly bool Valid;
        public readonly Vec2 FigureCenter;
        public readonly Vector3 WorldOrigin;
        public readonly float WorldScale;

        /// <summary>Outer edge of the card in world space (frame edge when the wooden frame is drawn, else the backdrop
        /// square, else the figure's bbox), counter-clockwise from bottom-left.</summary>
        public readonly Vector3[] FrameWorldCorners;

        public TargetCardPose(Vec2 figureCenter, Vector3 worldOrigin, float worldScale, Vector3[] frameWorldCorners)
        {
            Valid = true;
            FigureCenter = figureCenter;
            WorldOrigin = worldOrigin;
            WorldScale = worldScale;
            FrameWorldCorners = frameWorldCorners;
        }

        /// <summary>A point of the target (field world coordinates) as drawn on the card.</summary>
        public Vector3 ToWorld(Vec2 targetPoint) => WorldOrigin + new Vector3(
            (targetPoint.X - FigureCenter.X) * WorldScale,
            (targetPoint.Y - FigureCenter.Y) * WorldScale,
            0f);
    }
}
