using System;
using System.Collections.Generic;
using Gameplay.Sequence.Base;

namespace Gameplay.PlayScreen.Sequence
{
    /// <summary>
    /// Terminal sequence element (TASK-116, RFC §3): synchronously hides the banner and runs the campaign
    /// transition (the callback supplied by <see cref="Gameplay.Adapters.CampaignProgressionController"/>). Sync —
    /// <c>Finished</c> fires immediately after, driving the sequence to <c>Finish()</c> and clearing busy. Because
    /// the transition runs here, the next level is already loaded before the play screen is released.
    /// </summary>
    public sealed class TransitionElement : LevelSequenceElementBase
    {
        private readonly IPlayScreenView _view;
        private readonly Action _applyTransition;

        public TransitionElement(IPlayScreenView view, Action applyTransition)
        {
            _view = view;
            _applyTransition = applyTransition;
        }

        protected override void StartInternal(Dictionary<string, object> context)
        {
            _view.HideOutcome();
            _applyTransition?.Invoke();
        }
    }
}
