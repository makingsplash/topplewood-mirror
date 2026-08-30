using Gameplay.Core;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Visual register of a highlight (TASK-263 §2). The seam now serves TWO consumers with distinct feel, so the
    /// caller states which it wants rather than the view guessing:
    /// <list type="bullet">
    /// <item><see cref="FingerPoint"/> — the FTUE step-by-step tutorial (<c>AssembleInOrderStep</c>): a pointing
    /// finger sprite over a gently scale-pulsing contour. Unchanged from TASK-078.</item>
    /// <item><see cref="PulseOutline"/> — the Hint booster (<c>HintBoosterPresenter</c>): a BLINKING (alpha-pulse)
    /// silhouette contour, NO finger (owner feedback 2026-08-30 — a finger reads as "do the tutorial", a blinking
    /// outline reads as "this is the next piece").</item>
    /// </list>
    /// </summary>
    public enum HighlightStyle
    {
        FingerPoint,
        PulseOutline,
    }

    /// <summary>
    /// Highlight seam (TASK-049 RFC §Q3, TASK-083; TASK-263 §2 added <see cref="HighlightStyle"/>). A consumer reads
    /// piece poses AND shapes from <see cref="GameModel"/> (read-only) and asks the view to draw attention to a
    /// piece; the domain never imports the view (this seam carries domain data INTO the view, which is the permitted
    /// direction — <c>Gameplay.View</c> already depends on <c>Gameplay.Core</c>). Bound to <c>TutorialView</c>
    /// (TASK-078/083): a screen-space overlay that draws a green shape-matching CONTOUR (outline, not fill) above all
    /// pieces plus — in <see cref="HighlightStyle.FingerPoint"/> — a finger sprite, re-projecting the target each
    /// frame; <see cref="LoggingTutorialView"/> is kept as a headless/fallback stub. <see cref="Vec2"/> is the
    /// Unity-free domain vector; the view adapter converts on its side. NB: two consumers (tutorial + hint booster)
    /// share this seam, so the "Tutorial" name is now partly historical (TASK-244) — kept to avoid a namespace-wide
    /// import churn during a feature ticket; the <see cref="HighlightStyle"/> argument makes the dual use explicit.
    /// </summary>
    public interface ITutorialView
    {
        /// <summary>Draw attention to a piece whose pose is (<paramref name="origin"/>, <paramref name="rotation"/>)
        /// and whose geometry is <paramref name="shape"/>, in the requested visual <paramref name="style"/>. The view
        /// needs the shape+angle (not just a point) to trace a contour that matches the piece's silhouette, including
        /// rotated arc primitives (half-disc wheels). Rotation is in radians (domain convention, CCW positive).</summary>
        void Highlight(Vec2 origin, float rotation, Shape shape, HighlightStyle style);

        void Clear();
    }
}
