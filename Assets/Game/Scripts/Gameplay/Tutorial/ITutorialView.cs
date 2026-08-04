using Gameplay.Core;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Highlight seam for the tutorial system (TASK-049 RFC §Q3, TASK-083). The tutorial reads piece poses AND
    /// shapes from <see cref="GameModel"/> (read-only) and asks the view to draw attention to a piece; the domain
    /// never imports the view (this seam carries domain data INTO the view, which is the permitted direction —
    /// <c>Gameplay.View</c> already depends on <c>Gameplay.Core</c>). Bound to <c>TutorialView</c> (TASK-078/083):
    /// a screen-space overlay that draws a green shape-matching CONTOUR (outline, not fill) above all pieces plus a
    /// finger sprite, re-projecting the target each frame; <see cref="LoggingTutorialView"/> is kept as a headless/
    /// fallback stub. <see cref="Vec2"/> is the Unity-free domain vector; the view adapter converts on its side.
    /// </summary>
    public interface ITutorialView
    {
        /// <summary>Draw attention to a piece whose pose is (<paramref name="origin"/>, <paramref name="rotation"/>)
        /// and whose geometry is <paramref name="shape"/>. The view needs the shape+angle (not just a point) to
        /// trace a contour that matches the piece's silhouette, including rotated arc primitives (half-disc wheels).
        /// Rotation is in radians (domain convention, CCW positive).</summary>
        void Highlight(Vec2 origin, float rotation, Shape shape);

        void Clear();
    }
}
