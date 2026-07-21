using Gameplay.Core;
using Game.Services.Input;
using R3;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Bridges <see cref="IInputService"/> screen taps to domain <see cref="InputIntent"/> (overview
    /// §Адаптерная граница): normalises screen px → 0..1. One tap = one intent (one-tap). In v0 shape_fill
    /// the coordinates are unused (fixed drop spawn); the intent just means "release".
    /// </summary>
    public sealed class InputServiceGameplayInputSource : IGameplayInputSource
    {
        private readonly IInputService _inputService;

        public InputServiceGameplayInputSource(IInputService inputService)
        {
            _inputService = inputService;
            _inputService.Enable(); // idempotent (guarded inside InputService)
        }

        public Observable<InputIntent> Intents =>
            _inputService.OnClickedAsObservable().Select(ToIntent);

        private static InputIntent ToIntent(Vector2 screenPosition)
        {
            var w = Screen.width > 0 ? Screen.width : 1;
            var h = Screen.height > 0 ? Screen.height : 1;
            return new InputIntent(screenPosition.x / w, screenPosition.y / h);
        }
    }
}
