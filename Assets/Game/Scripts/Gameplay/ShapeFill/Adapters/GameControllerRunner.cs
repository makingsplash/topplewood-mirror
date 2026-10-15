using Gameplay.Core;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Single Unity contact of the tick loop (overview §Тик-луп): forwards engine Start/Tick into the
    /// Unity-free <see cref="GameController"/>. Real delta feeds the domain fixed-step clock.
    ///
    /// While <see cref="GameplayPause"/> is held (a modal screen is up, TASK-566) the WHOLE tick is skipped, clock
    /// advance included: the paused time never reaches the accumulator, so closing the screen does not release a
    /// catch-up burst of physics steps.
    /// </summary>
    public sealed class GameControllerRunner : IStartable, ITickable
    {
        private readonly GameController _controller;
        private readonly GameplayPause _pause;

        public GameControllerRunner(GameController controller, GameplayPause pause)
        {
            _controller = controller;
            _pause = pause;
        }

        public void Start() => _controller.Start();

        public void Tick()
        {
            if (_pause.IsPaused)
                return;
            _controller.Tick(Time.deltaTime);
        }
    }
}
