using System;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Fits the orthographic <c>Camera.main</c> to the level content on load (TASK-009, RFC 07 §1). The
    /// bringup scene's camera is fixed, so tall levels (L5 snowman stacks to y≈8) fall outside the viewport
    /// and their top pieces become untappable → the level is unwinnable. This view-side seam frames the union
    /// AABB of the pieces + target slots + static geometry so every piece is on screen and tappable.
    ///
    /// Registered BEFORE <see cref="TargetPreviewPresenter"/> (RFC 07 §1.1): the preview anchors to a
    /// <c>Camera.main</c> viewport point, so the camera must be framed first. Both re-fire on LevelLoaded, so a
    /// reload (WIN→next / retry) reframes for the new level. Mirrors <see cref="TargetPreviewPresenter"/>:
    /// observes <see cref="GameModel"/> only — the domain stays view-agnostic and headless-safe.
    /// </summary>
    public sealed class CameraFramingPresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly CameraFramingConfig _config;
        private IDisposable _subscription;

        public CameraFramingPresenter(GameModel model, CameraFramingConfig config)
        {
            _model = model;
            _config = config;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => Frame());

            // A level may already be loaded before this entry point starts (GameControllerRunner runs first and
            // fires LevelLoaded synchronously). Frame the current level now — Frame self-guards on null.
            Frame();
        }

        private void Frame()
        {
            var level = _model.CurrentLevel;
            if (level == null)
                return;

            var camera = UnityEngine.Camera.main;
            if (camera == null || !camera.orthographic)
                return;

            if (!CameraFramingMath.TryContentBounds(level, out var min, out var max))
                return;

            var centerX = (min.X + max.X) * 0.5f;
            var centerY = (min.Y + max.Y) * 0.5f;

            // Framing math lives in Core (CameraFramingMath, TASK-085) so the validator predicts the same orthoSize;
            // the presenter keeps only the Unity contact. Reserve the top PreviewBandFraction for the target preview:
            // content is framed into the lower (1 - band) of the viewport, then the camera is raised by size*band so
            // the top band stays empty for the preview card.
            var size = CameraFramingMath.ComputeOrthoSize(
                min, max, camera.aspect, _config.Margin, _config.PreviewBandFraction, _config.MinOrthoSize);
            camera.orthographicSize = size;

            var t = camera.transform;
            t.position = new Vector3(centerX, centerY + size * _config.PreviewBandFraction, t.position.z);
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
