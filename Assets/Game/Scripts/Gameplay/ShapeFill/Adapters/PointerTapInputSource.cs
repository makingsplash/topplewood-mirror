using Gameplay.Core;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Input source for the isolated bringup scope (RFC §2.3): reads the new Input System directly with no
    /// <c>IInputService</c>/<c>Data</c> dependency. On a touch device it reads every began touch of
    /// <see cref="Touchscreen.current"/>; in editor/mouse it falls back to <see cref="Pointer.current"/>.
    /// One began touch = one <see cref="InputIntent"/>, so a single frame may emit several — the balance
    /// coalescence group (TASK-023) needs two within its window, and holding finger&#160;1 no longer blocks
    /// finger&#160;2's began edge (TASK-108). Each press position is projected through the main camera to a
    /// world-space point (X,Y) (TASK-007 pivot): the domain hit-tests which Idle piece was tapped and releases
    /// it. Ticked as a VContainer entry point.
    /// </summary>
    public sealed class PointerTapInputSource : IGameplayInputSource, ITickable
    {
        private readonly Subject<InputIntent> _intents = new();

        // Fully qualified on purpose: the enclosing Gameplay namespace has a sibling namespace
        // Gameplay.Camera, so the simple name "Camera" binds to that namespace (CS0118). A using-alias
        // does not win over the enclosing-namespace member, so we spell out UnityEngine.Camera everywhere.
        private UnityEngine.Camera _camera;

        public Observable<InputIntent> Intents => _intents;

        public void Tick()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                // On a touch device Pointer.current is an alias of the PRIMARY touch, so reading it here would
                // silently drop finger 2 and double-count finger 1. Each TouchControl has its own press edge:
                // two fingers landing in the same frame emit two intents, and a finger landing while another is
                // held still raises ITS OWN wasPressedThisFrame — which is exactly the balance-mode gesture the
                // aggregate Pointer could not deliver (TASK-108). Do NOT also read Pointer.current here.
                foreach (var touch in touchscreen.touches)
                {
                    if (touch.press.wasPressedThisFrame)
                        Emit(touch.position.ReadValue());
                }

                return;
            }

            // Editor / mouse: no touchscreen present — one press = one intent, unchanged.
            var pointer = Pointer.current;
            if (pointer == null)
                return;

            if (pointer.press.wasPressedThisFrame)
                Emit(pointer.position.ReadValue());
        }

        private void Emit(Vector2 screen)
        {
            var worldX = 0f;
            var worldY = 0f;
            var camera = ResolveCamera();
            if (camera != null)
            {
                // z = distance from the camera to the z=0 gameplay plane (ortho ignores it for x/y, but this
                // stays correct if the camera ever goes perspective).
                var world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z));
                worldX = world.x;
                worldY = world.y;
            }

            _intents.OnNext(new InputIntent(worldX, worldY));
        }

        private UnityEngine.Camera ResolveCamera()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            return _camera;
        }
    }
}
