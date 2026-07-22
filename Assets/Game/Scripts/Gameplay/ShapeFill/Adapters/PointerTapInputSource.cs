using Gameplay.Core;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;
using Camera = UnityEngine.Camera; // disambiguate from a package "Camera" namespace in scope

namespace Gameplay.Adapters
{
    /// <summary>
    /// Raw one-tap input source for the isolated bringup scope (RFC §2.3): reads the new Input System
    /// <see cref="Pointer"/> directly (mouse or touch) with no <c>IInputService</c>/<c>Data</c> dependency.
    /// One press = one <see cref="InputIntent"/>. The press position is projected through the main camera to
    /// a world-space X (v0 drop mechanic, TASK-007 rework): the tap chooses the drop column so blocks can
    /// spread across the silhouette. Ticked as a VContainer entry point.
    /// </summary>
    public sealed class PointerTapInputSource : IGameplayInputSource, ITickable
    {
        private readonly Subject<InputIntent> _intents = new();

        private Camera _camera;

        public Observable<InputIntent> Intents => _intents;

        public void Tick()
        {
            var pointer = Pointer.current;
            if (pointer == null)
                return;

            if (!pointer.press.wasPressedThisFrame)
                return;

            var worldX = 0f;
            var camera = ResolveCamera();
            if (camera != null)
            {
                var screen = pointer.position.ReadValue();
                // z = distance from the camera to the z=0 gameplay plane (ortho ignores it for x/y, but this
                // stays correct if the camera ever goes perspective).
                var world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z));
                worldX = world.x;
            }

            _intents.OnNext(new InputIntent(worldX, 0f));
        }

        private Camera ResolveCamera()
        {
            if (_camera == null)
                _camera = Camera.main;
            return _camera;
        }
    }
}
