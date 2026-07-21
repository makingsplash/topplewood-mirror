using Gameplay.Core;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Single Unity contact of the tick loop (overview §Тик-луп): forwards engine Start/Tick into the
    /// Unity-free <see cref="GameController"/>. Real delta feeds the domain fixed-step clock.
    /// </summary>
    public sealed class GameControllerRunner : IStartable, ITickable
    {
        private readonly GameController _controller;

        public GameControllerRunner(GameController controller)
        {
            _controller = controller;
        }

        public void Start() => _controller.Start();

        public void Tick() => _controller.Tick(Time.deltaTime);
    }
}
