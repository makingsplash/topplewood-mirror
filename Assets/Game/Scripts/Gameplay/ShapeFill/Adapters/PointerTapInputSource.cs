using Gameplay.Core;
using R3;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Raw one-tap input source for the isolated bringup scope (RFC §2.3): reads the new Input System
    /// <see cref="Pointer"/> directly (mouse or touch) with no <c>IInputService</c>/<c>Data</c> dependency.
    /// One press = one <see cref="InputIntent"/> ("release the block"); coordinates are unused in v0.
    /// Ticked as a VContainer entry point.
    /// </summary>
    public sealed class PointerTapInputSource : IGameplayInputSource, ITickable
    {
        private readonly Subject<InputIntent> _intents = new();

        public Observable<InputIntent> Intents => _intents;

        public void Tick()
        {
            var pointer = Pointer.current;
            if (pointer == null)
                return;

            if (pointer.press.wasPressedThisFrame)
                _intents.OnNext(new InputIntent(0f, 0f));
        }
    }
}
