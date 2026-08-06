using System;
using System.Threading;
using Gameplay.Core;
using SGS.Infrastructure.Sequence;

namespace Gameplay.PlayScreen.Sequence
{
    /// <summary>
    /// The WIN/LOSE play-screen sequence (TASK-116, RFC §3) — the first real <c>GameSequence</c> subclass. Three
    /// composed elements: <see cref="OutcomeBannerElement"/> (async banner intro) → <see cref="HoldElement"/>
    /// (async static hold, the owner's "+ 2 seconds") → <see cref="TransitionElement"/> (sync: hide banner + run
    /// the campaign transition). Hold is a SEPARATE element, not folded into the banner, because the owner
    /// requirement is two composable steps ("animation plays + 2 more seconds of static"). Overrides
    /// <c>Finish()</c> to raise <see cref="Completed"/> (same pattern as the template's <c>InitGameSequence</c>).
    /// </summary>
    public sealed class LevelOutcomeSequence : GameSequence
    {
        public event Action Completed;

        public LevelOutcomeSequence(
            IPlayScreenView view, PlayScreenConfig config, GameOutcome outcome, Action applyTransition, CancellationToken ct)
        {
            Elements.Add(new OutcomeBannerElement(view, outcome, ct));
            Elements.Add(new HoldElement(config.OutcomeHoldSeconds, ct));
            Elements.Add(new TransitionElement(view, applyTransition));
        }

        protected override void Finish()
        {
            Completed?.Invoke();
            base.Finish();
        }
    }
}
