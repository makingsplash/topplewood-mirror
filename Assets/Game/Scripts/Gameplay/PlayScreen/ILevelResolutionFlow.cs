using System;
using Gameplay.Core;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Unity-side seam (TASK-116, RFC §2.2) that inverts *when* a resolved level transitions. The domain never
    /// sees this interface: <see cref="Gameplay.Adapters.CampaignProgressionController"/> stays the single
    /// <c>LevelResolved</c> subscriber and the sole owner of the campaign index/persist/reload, but instead of
    /// transitioning inline it hands the flow its own transition as a callback and lets the flow decide *when*
    /// to invoke it. Prod (<see cref="GatedResolutionFlow"/>) gates BOTH outcomes with a screen — a lose screen on
    /// Lost, a win screen on Won (TASK-157) — after a settle-beat; bringup (<see cref="SequencedResolutionFlow"/>)
    /// plays the WIN/LOSE banner + hold first. The flow never duplicates the "won ? advance : reload" logic —
    /// that lives only in the campaign controller (one source of truth).
    /// </summary>
    public interface ILevelResolutionFlow
    {
        /// <param name="outcome">Terminal outcome, for the banner/screen (Won/Lost).</param>
        /// <param name="applyTransition">The campaign controller's private transition; invoke to actually advance/reload.</param>
        /// <param name="isFinalLevel">
        /// Presentational meta-info only (TASK-157): the win screen shows the end-of-line-up copy ("ALL DONE!"/"Next")
        /// instead of the normal copy when this is the last level of the FIRST pass of the line-up (TASK-528: once per
        /// game — catch-up and pool levels are never final; the tap enters the pool). It is "what to show", NOT
        /// "what to do" — the actual won?advance:reload stays in the controller, so the seam invariant holds. The
        /// bringup flow ignores it (its banner is outcome-only).
        /// </param>
        void Present(GameOutcome outcome, Action applyTransition, bool isFinalLevel);
    }
}
