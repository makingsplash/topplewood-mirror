using System;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using R3;
using UnityEngine;
using VContainer.Unity;
using Game.Services.LogService;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Unity-side orchestrator for the tutorial system (TASK-049 RFC §3), registered as an entry point in the
    /// context-independent core installer. It lives OUTSIDE the domain: the headless simulation builds the
    /// domain graph with bare <c>new</c> and never registers this runner, so the sim physically cannot play the
    /// tutorial (RFC §7 invariant 2). On each <see cref="GameModel.LevelLoaded"/> it re-evaluates the gate and
    /// (re)starts the tour from step 0 when eligible — so a LOSS reload of the tutorial level replays the tour,
    /// and completion is persisted only after the LAST step (mid-tour LOSS ⇒ tour repeats, RFC §Q4).
    ///
    /// A synchronously-completing step is supported by design: a step that observes a <c>ReactiveProperty</c>
    /// (e.g. <see cref="GameModel.Phase"/>/<see cref="GameModel.Outcome"/>, which replay their current value on
    /// Subscribe) can fire completion INSIDE <see cref="ITutorialStep.Enter"/>'s Subscribe call. Advancement is
    /// therefore reentrancy-safe: a completion that arrives during arming is deferred to the arming loop instead
    /// of recursing, so the next step's subscription is never overwritten and orphaned (QA §4/C).
    /// </summary>
    public sealed class TutorialRunner : IStartable, IDisposable
    {
        // Per-level tours (TASK-377, closing the TASK-112 follow-up): the tour to play is resolved from the LOADED
        // level's LevelId via the TutorialCatalog, NOT from a campaign index. Keying on the loaded LevelId keeps the
        // gate coherent with a DEV level-jump (a non-destructive preview that never writes the pointer) and with any
        // re-ordering of the manifest — a jumped-to or moved level plays exactly the tour registered for its id, or
        // none. sailboat → assemble-in-order FTUE; grand_seesaw → the simultaneous co-release tour.
        private readonly GameModel _model;
        private readonly ITutorialGate _gate;
        private readonly ITutorialProgressStore _progress;
        private readonly ITutorialAnalytics _analytics;
        private readonly ITutorialView _view;
        private readonly ITutorialInputGate _inputGate;
        private readonly IGameplayInputSource _input;
        private readonly IGameplayTapTolerance _tapTolerance;
        private readonly TutorialCatalog _catalog;

        private IDisposable _levelLoadedSubscription;
        private IDisposable _stepSubscription;
        private TutorialContext _context;
        private TutorialDefinition _definition; // the tour resolved for the currently-loaded level (null ⇒ none)
        private int _stepIndex;

        // Reentrancy guard for a step that completes synchronously during Enter's Subscribe (see class summary):
        // OnStepCompleted then sets _advancePending instead of recursing, and the PlayCurrentStep loop advances
        // only after _stepSubscription has been safely assigned.
        private bool _arming;
        private bool _advancePending;

        public TutorialRunner(
            GameModel model,
            ITutorialGate gate,
            ITutorialProgressStore progress,
            ITutorialAnalytics analytics,
            ITutorialView view,
            ITutorialInputGate inputGate,
            IGameplayInputSource input,
            IGameplayTapTolerance tapTolerance,
            TutorialCatalog catalog)
        {
            _model = model;
            _gate = gate;
            _progress = progress;
            _analytics = analytics;
            _view = view;
            _inputGate = inputGate;
            _input = input;
            _tapTolerance = tapTolerance;
            _catalog = catalog;
        }

        public void Start()
        {
            _context = new TutorialContext(_model, _view, _inputGate, _input.Intents, _tapTolerance);
            _levelLoadedSubscription = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());

            // A level is already loaded before this entry point starts (GameControllerRunner + the campaign
            // controller run first and fire LevelLoaded synchronously), so evaluate the current state once now —
            // exactly how ShapeFillPresenter catches the missed initial load.
            OnLevelLoaded();
        }

        private void OnLevelLoaded()
        {
            // Re-evaluate on every load: cancel any in-progress tour, resolve the tour for THIS level, then (re)start
            // from step 0 if still eligible. Idempotent with the reset-in-place reload the campaign controller does on
            // LOSS.
            StopSequence();
            _definition = ResolveDefinition();
            if (_definition == null || !ShouldPlay())
                return;
            StartSequence();
        }

        // The tour registered for the loaded level's id, or null if the level has none (→ no tour plays).
        private TutorialDefinition ResolveDefinition() =>
            _model.CurrentLevel == null ? null : _catalog.Resolve(_model.CurrentLevel.LevelId);

        private bool ShouldPlay()
        {
            return _gate.Enabled
                && !_progress.IsCompleted(_definition.Id);
        }

        private void StartSequence()
        {
            _stepIndex = 0;
            PlayCurrentStep();
        }

        private void PlayCurrentStep()
        {
            // Loop rather than recurse so a step that completes synchronously (during Subscribe, below) advances
            // here — AFTER its subscription is assigned to _stepSubscription — instead of re-entering and letting
            // the next step's subscription be overwritten by the just-assigned dead one.
            while (true)
            {
                if (_stepIndex >= _definition.Steps.Count)
                {
                    CompleteTutorial();
                    return;
                }

                var step = _definition.Steps[_stepIndex];
                _analytics.LogStep(_definition.Id, step.Id);

                _stepSubscription?.Dispose();
                _advancePending = false;
                _arming = true;
                // Take(1): the runner owns "advance on first completion" so steps can return any completion stream.
                _stepSubscription = step.Enter(_context)
                    .Take(1)
                    .Subscribe(_ => OnStepCompleted(step));
                _arming = false;

                // Asynchronous completion (the common case): return and wait for the emission to call back.
                if (!_advancePending)
                    return;
                // Synchronous completion fired inside Subscribe: OnStepCompleted already ran Exit + advanced the
                // index, so loop to arm the next step now that _stepSubscription points at this (finished) one.
            }
        }

        private void OnStepCompleted(ITutorialStep step)
        {
            step.Exit(_context);
            _stepIndex++;
            if (_arming)
            {
                // Completed synchronously during arming — defer to the PlayCurrentStep loop (see above).
                _advancePending = true;
                return;
            }
            PlayCurrentStep();
        }

        private void CompleteTutorial()
        {
            _progress.MarkCompleted(_definition.Id); // durable — survives a kill
            _analytics.LogComplete(_definition.Id);
            _view.Clear();
            Debug.unityLogger.Log(LogType.Log, $"[{LogCategory.Tutorial}] Tutorial '{_definition.Id}' completed");
        }

        private void StopSequence()
        {
            _stepSubscription?.Dispose();
            _stepSubscription = null;
            _view.Clear();
        }

        public void Dispose()
        {
            _levelLoadedSubscription?.Dispose();
            _levelLoadedSubscription = null;
            StopSequence();
        }
    }
}
