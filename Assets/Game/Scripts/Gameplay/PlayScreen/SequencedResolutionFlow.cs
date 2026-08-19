using System;
using System.Threading;
using Gameplay.Core;
using Gameplay.PlayScreen.Sequence;
using Gameplay.Sequence;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Bringup <see cref="ILevelResolutionFlow"/> (TASK-116, RFC §2.2/§3): the first real consumer of the
    /// <c>GameSequence</c> system. On each resolution it marks the play screen busy, builds a
    /// <see cref="LevelOutcomeSequence"/> through <see cref="ISequenceFactory"/> (banner → 2s hold → hide +
    /// transition), and runs it; the transition callback (the campaign controller's own advance/reload) fires
    /// inside the sequence's terminal element, so by the time the hold ends the new level is already loaded, and
    /// only then is <see cref="PlayScreenInteraction.IsBusy"/> cleared (Restart re-enables on the new level).
    ///
    /// Teardown mid-hold (scope/session dispose): the shared <see cref="CancellationToken"/> is cancelled, so the
    /// awaited banner/hold throw <see cref="OperationCanceledException"/>, <c>Finished</c> never fires, and the
    /// transition is (correctly) NOT run. Only ever one sequence is live at a time — the domain is frozen at
    /// Won/Lost until the reload, and the next resolution cannot fire before that reload — so no overlap.
    /// </summary>
    public sealed class SequencedResolutionFlow : ILevelResolutionFlow, IDisposable
    {
        private readonly ISequenceFactory _factory;
        private readonly IPlayScreenView _view;
        private readonly PlayScreenConfig _config;
        private readonly PlayScreenInteraction _interaction;
        private readonly CancellationTokenSource _cts = new();

        public SequencedResolutionFlow(
            ISequenceFactory factory, IPlayScreenView view, PlayScreenConfig config, PlayScreenInteraction interaction)
        {
            _factory = factory;
            _view = view;
            _config = config;
            _interaction = interaction;
        }

        // isFinalLevel is presentational meta for the prod win screen (TASK-157); the bringup banner is
        // outcome-only, so it is deliberately ignored here.
        public void Present(GameOutcome outcome, Action applyTransition, bool isFinalLevel)
        {
            _interaction.IsBusy.Value = true;

            var sequence = _factory.CreateGameSequence<LevelOutcomeSequence>(
                _view, _config, outcome, applyTransition, _cts.Token);
            sequence.Completed += OnSequenceCompleted;
            sequence.Start();
        }

        private void OnSequenceCompleted() => _interaction.IsBusy.Value = false;

        public void Dispose()
        {
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            _cts.Dispose();
        }
    }
}
