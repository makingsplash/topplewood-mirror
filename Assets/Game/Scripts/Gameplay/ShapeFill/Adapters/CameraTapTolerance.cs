using Gameplay.Core;
using Gameplay.Core.Adapters;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Runtime binding of <see cref="IGameplayTapTolerance"/> (TASK-085): converts the owner's screen-space tap
    /// target (<see cref="TapZoneConfig.MinTapDp"/>, dp) into the world-space halo RADIUS the domain hit-test's
    /// fallback tier uses, at the LIVE per-level camera framing. Because <c>CameraFramingPresenter</c> reframes the
    /// orthographic camera to each level's content, world-per-pixel differs level to level — so the conversion must
    /// read the live <c>orthographicSize</c>, never a world constant (that would give a different physical zone on a
    /// giraffe vs a locomotive, the whole point of the requirement).
    ///
    /// Ortho camera: worldPerPixel = 2·orthoSize / screenPixelHeight; dp→px = dp·(dpi/160). So the world DIAMETER is
    /// MinTapDp·(dpi/160)·2·orthoSize/screenHeight and the halo radius is half that. Recomputed per read (taps are
    /// rare — no caching needed beyond the Camera.main handle) so a reframe is reflected immediately. Registered in
    /// <c>GameplayCoreInstaller</c> beside <c>CameraFramingPresenter</c>, which also reads Camera.main.
    /// </summary>
    public sealed class CameraTapTolerance : IGameplayTapTolerance
    {
        private readonly TapZoneConfig _config;

        // Fully qualified: the enclosing Gameplay namespace has a sibling Gameplay.Camera, so the bare name
        // "Camera" binds to that namespace (CS0118). Same guard as PointerTapInputSource.
        private UnityEngine.Camera _camera;

        public CameraTapTolerance(TapZoneConfig config)
        {
            _config = config;
        }

        public float WorldRadius
        {
            get
            {
                var camera = ResolveCamera();
                if (camera == null || !camera.orthographic)
                    return 0f;

                var screenHeight = UnityEngine.Screen.height;
                if (screenHeight <= 0)
                    return 0f;

                // Some devices report Screen.dpi == 0 — fall back to the reference dpi so the halo is still sane.
                var dpi = UnityEngine.Screen.dpi;
                if (dpi <= 0f)
                    dpi = _config.RefDpi;

                var worldPerPixel = 2f * camera.orthographicSize / screenHeight;
                var diameterPx = _config.MinTapDp * (dpi / 160f);
                var diameterWorld = diameterPx * worldPerPixel;
                return diameterWorld * 0.5f;
            }
        }

        private UnityEngine.Camera ResolveCamera()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            return _camera;
        }
    }
}
