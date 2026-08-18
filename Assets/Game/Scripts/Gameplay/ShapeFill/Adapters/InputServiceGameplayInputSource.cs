using Gameplay.Core;
using Game.Services.Input;
using R3;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Bridges <see cref="IInputService"/> screen taps to domain <see cref="InputIntent"/> (overview
    /// §Адаптерная граница). Since the TASK-007 pivot the intent carries the tapped point in WORLD/level space
    /// (<see cref="InputIntent"/> doc): the mechanic hit-tests which Idle piece POLYGON the point lands in
    /// (<c>PickIdlePieceAt</c> → <c>PieceHitResolver</c>, world-space vertices), so the screen tap MUST be
    /// projected through <c>Camera.main</c> exactly as the bringup <see cref="PointerTapInputSource"/> does.
    /// TASK-138: this adapter previously NORMALISED screen px → 0..1 — a stale relic of the pre-pivot
    /// "coordinates unused, fixed drop spawn" contract. A 0..1 point never lands inside a world-space polygon
    /// (piece positions run several world units around the origin, often negative), so every gameplay tap
    /// silently missed. It never surfaced because the prod scope was never instantiated until TASK-138 raised it;
    /// the first prod boot exposed it (same latent-defect class as the camera-projection and UiService findings).
    /// One tap = one intent (one-tap).
    /// </summary>
    public sealed class InputServiceGameplayInputSource : IGameplayInputSource
    {
        private readonly IInputService _inputService;

        // Fully qualified: the enclosing Gameplay namespace has a sibling Gameplay.Camera, so the bare name
        // "Camera" binds to that namespace (CS0118). Same guard as PointerTapInputSource / CameraTapTolerance.
        private UnityEngine.Camera _camera;

        public InputServiceGameplayInputSource(IInputService inputService)
        {
            _inputService = inputService;
            _inputService.Enable(); // idempotent (guarded inside InputService)
        }

        public Observable<InputIntent> Intents =>
            _inputService.OnClickedAsObservable().Select(ToIntent);

        private InputIntent ToIntent(Vector2 screenPosition)
        {
            var camera = ResolveCamera();
            if (camera == null)
                return new InputIntent(0f, 0f);

            // z = distance from the camera to the z=0 gameplay plane (ortho ignores it for x/y, but this stays
            // correct if the camera ever goes perspective). Mirrors PointerTapInputSource.Emit exactly.
            var world = camera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
            return new InputIntent(world.x, world.y);
        }

        private UnityEngine.Camera ResolveCamera()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            return _camera;
        }
    }
}
