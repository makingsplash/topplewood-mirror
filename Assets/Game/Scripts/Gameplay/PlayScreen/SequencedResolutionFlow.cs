using System;
using System.Threading;
using Gameplay.Core;
using Gameplay.PlayScreen.Sequence;
using Gameplay.Sequence;
using R3;

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
        private readonly WinInterlude _interlude;
        private readonly CancellationTokenSource _cts = new();
        private readonly SerialDisposable _interludeSubscription = new();

        public SequencedResolutionFlow(
            ISequenceFactory factory, IPlayScreenView view, PlayScreenConfig config, PlayScreenInteraction interaction,
            WinInterlude interlude)
        {
            _factory = factory;
            _view = view;
            _config = config;
            _interaction = interaction;
            _interlude = interlude;
        }

        // isFinalLevel is presentational meta for the prod win screen (TASK-157); the bringup banner is
        // outcome-only, so it is deliberately ignored here.
        public void Present(GameOutcome outcome, Action applyTransition, bool isFinalLevel)
        {
            _interaction.IsBusy.Value = true;

            // TASK-613: an armed win interlude (the tour's "Совпало" beat) plays BEFORE the banner, same contract as
            // the prod GatedResolutionFlow — without this the beat would never finish in bringup and the tour would
            // replay on every visit. Ceiling: PlayScreenConfig.MaxWinInterludeSeconds.
            if (outcome == GameOutcome.Won && _interlude.TryTake(out var play))
            {
                PlayInterlude(play, () => StartSequence(outcome, applyTransition));
                return;
            }
            StartSequence(outcome, applyTransition);
        }

        private void PlayInterlude(Func<Observable<Unit>> play, Action then)
        {
            var finished = false;
            void Finish(bool timedOut)
            {
                if (finished)
                    return;
                finished = true;
                if (timedOut)
                    UnityEngine.Debug.unityLogger.Log(UnityEngine.LogType.Warning,
                        $"[PlayScreen] win interlude did not finish in {_config.MaxWinInterludeSeconds:0.#}s — showing the banner");
                _interludeSubscription.Disposable = null; // stop the number (and its ceiling) before the banner
                then();
            }

            var ceiling = Observable.Timer(TimeSpan.FromSeconds(_config.MaxWinInterludeSeconds))
                .Subscribe(_ => Finish(true));
            var number = play().Subscribe(_ => { }, _ => Finish(false));
            if (!finished)
                _interludeSubscription.Disposable = Disposable.Combine(number, ceiling);
            else
            {
                number.Dispose();
                ceiling.Dispose();
            }
        }

        private void StartSequence(GameOutcome outcome, Action applyTransition)
        {
            var sequence = _factory.CreateGameSequence<LevelOutcomeSequence>(
                _view, _config, outcome, applyTransition, _cts.Token);
            sequence.Completed += OnSequenceCompleted;
            sequence.Start();
        }

        private void OnSequenceCompleted() => _interaction.IsBusy.Value = false;

        public void Dispose()
        {
            _interludeSubscription.Dispose();
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            _cts.Dispose();
        }
    }
}
