using System;
using Gameplay.Core;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Unity-side seam (TASK-116, RFC §2.2) that inverts *when* a resolved level transitions. The domain never
    /// sees this interface: <see cref="Gameplay.Adapters.CampaignProgressionController"/> stays the single
    /// <c>LevelResolved</c> subscriber and the sole owner of the campaign index/persist/reload, but instead of
    /// transitioning inline it hands the flow its own transition as a callback and lets the flow decide *when*
    /// to invoke it. Prod (<see cref="ImmediateResolutionFlow"/>) runs it at once — behaviour-preserving; bringup
    /// (<see cref="SequencedResolutionFlow"/>) plays the WIN/LOSE banner + hold first. The flow never duplicates
    /// the "won ? advance : reload" logic — that lives only in the campaign controller (one source of truth).
    /// </summary>
    public interface ILevelResolutionFlow
    {
        /// <param name="outcome">Terminal outcome, for the banner (Won/Lost).</param>
        /// <param name="applyTransition">The campaign controller's private transition; invoke to actually advance/reload.</param>
        void Present(GameOutcome outcome, Action applyTransition);
    }
}
