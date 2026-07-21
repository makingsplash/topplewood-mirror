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

            var level = _levelSource.Load();
            _model.LoadLevel(level); // mechanic reacts to LevelLoaded: installs statics, spawns first block.
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
