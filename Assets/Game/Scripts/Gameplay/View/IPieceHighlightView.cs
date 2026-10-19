using Gameplay.Core;

namespace Gameplay.View
{
    /// <summary>
    /// The Hint booster's highlight seam (TASK-613 plan §3.4; was the shared <c>ITutorialView</c> with
    /// <c>HighlightStyle.PulseOutline</c>): a BLINKING silhouette contour on one piece, no finger — a finger reads as
    /// "do the tutorial", a blinking outline as "this is the next piece" (owner 2026-08-30). Its only writer is
    /// <c>HintBoosterPresenter</c>; the tour has its own <c>ITourView</c>, so <see cref="Clear"/> here can never tear down
    /// a tour (TASK-382/383). Domain data flows INTO the view; rotation in radians, CCW positive.
    /// </summary>
    public interface IPieceHighlightView
    {
        void Show(Vec2 origin, float rotation, Shape shape);

        void Clear();
    }
}
