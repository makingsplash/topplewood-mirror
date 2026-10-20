using Gameplay.Core;

namespace Gameplay.View
{
    /// <summary>
    /// The Hint booster's highlight seam (TASK-613 plan §3.4; was the shared <c>ITutorialView</c> with
    /// <c>HighlightStyle.PulseOutline</c>): a glowing halo around one piece's silhouette, no finger — a finger reads as
    /// "do the tutorial", an outline as "this is the next piece" (owner 2026-08-30; the halo replaced the blinking green
    /// contour in TASK-641). Its only writer is
    /// <c>HintBoosterPresenter</c>; the tour has its own <c>ITourView</c>, so <see cref="Clear"/> here can never tear down
    /// a tour (TASK-382/383). Domain data flows INTO the view; rotation in radians, CCW positive.
    /// </summary>
    public interface IPieceHighlightView
    {
        /// <param name="placementIndex">The piece's index in the level (<c>PieceInstance.PlacementIndex</c>): TASK-641
        /// tints that piece's own view a little toward its active colour while the halo is up.</param>
        void Show(Vec2 origin, float rotation, Shape shape, int placementIndex);

        void Clear();
    }
}
