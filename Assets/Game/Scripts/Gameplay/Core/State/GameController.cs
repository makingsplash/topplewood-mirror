using System;
using R3;

namespace Gameplay.Core
{
    /// <summary>
    /// Domain-side gameplay orchestrator (overview §Тик-луп). Pure Start()/Tick(dt): QA drives it
    /// synthetically. Loads the level, routes input intents to the mechanic, and runs the fixed-step
    /// loop (clock → simulation.Step + mechanic.Tick). Physics acceleration is by step COUNT, never by
    /// enlarging FixedStep (ADR-0001).
    /// </summary>
    public sealed class GameController : IDisposable
    {
        private readonly ILevelSource _levelSource;
        private readonly GameModel _model;
        private readonly IMechanicController _mechanic;
        private readonly IGameplaySimulation _simulation;
        private readonly IGameplayClock _clock;
        private readonly IGameplayInputSource _inputSource;

        private IDisposable _inputSubscription;
        private bool _started;

        /// <summary>
        /// Optional per-fixed-step diagnostic hook (TASK-070). Left null in prod and the headless sim, so the
        /// tick loop's null-conditional call is skipped and the pose sequence / poseHash are unchanged. A dev
        /// build attaches a read-only pose logger here (see <see cref="IStepObserver"/>).
        /// </summary>
        public IStepObserver StepObserver { get; set; }

        public GameController(
            ILevelSource levelSource,
            GameModel model,
            IMechanicController mechanic,
            IGameplaySimulation simulation,
            IGameplayClock clock,
            IGameplayInputSource inputSource)
        {
            _levelSource = levelSource;
            _model = model;
            _mechanic = mechanic;
            _simulation = simulation;
            _clock = clock;
            _inputSource = inputSource;
        }

        public void Start()
        {
            if (_started)
                return;
            _started = true;

            _inputSubscription = _inputSource.Intents.Subscribe(OnIntent);

            // Load the first level; the campaign controller (if present) resumes to the persisted index and
            // drives subsequent reloads. In-level progress is NOT restored (TASK-009: kill mid-level = fresh).
            ReloadLevel(0);
        }

        /// <summary>
        /// Load level <paramref name="index"/> from scratch (TASK-009). Re-entrant: the mechanic's LevelLoaded
        /// handler already clears the runtime, resets the simulation, reloads statics and re-spawns, so
        /// advancing/retrying needs no extra teardown (dev-architect 17:25 §4).
        /// </summary>
        public void ReloadLevel(int index)
        {
            var level = _levelSource.Load(index);
            _model.LoadLevel(level); // mechanic reacts to LevelLoaded: installs statics, pre-places all pieces.
        }

        public void Tick(float dt)
        {
            if (!_started)
                return;

            _clock.Advance(dt);
            while (_clock.TryConsumeStep())
            {
                _simulation.Step(_clock.FixedStep);
                _mechanic.Tick(_clock.FixedStep);
                // Diagnostic-only, after the pose mirror is current for this step; null in prod/sim (no cost,
                // no effect on the pose sequence). Read-only by IStepObserver contract (TASK-070).
                StepObserver?.OnStep();
            }
        }

        private void OnIntent(InputIntent intent) => _mechanic.OnIntent(intent);

        public void Dispose()
        {
            _inputSubscription?.Dispose();
            _inputSubscription = null;
        }
    }
}
