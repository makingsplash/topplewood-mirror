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
    /// and their top pieces become untappable → the level is unwinnable. This view-side seam frames the box
    /// <see cref="CameraFramingMath.TryContentBounds"/> returns — the pieces + target slots + air zones, with static
    /// geometry counted only vertically inside their X span (TASK-442: a wide floor runs off the screen edges
    /// instead of shrinking the level) — so every piece is on screen and tappable.
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
            if (camera == null)
                return;

            // ComputeOrthoSize frames content by writing camera.orthographicSize — a perspective camera IGNORES
            // that, so the level renders in its default perspective frame while all downstream gameplay/view code
            // assumes it was fitted (pieces off-screen, target card overlapped, RESTART covered). This silently
            // mis-framed the prod Loading camera for a full iteration (TASK-138). Fail LOUD with a named cause
            // instead of a silent early-return: the scene's Camera.main must be orthographic (as in bringup).
            if (!camera.orthographic)
            {
                Debug.LogError(
                    $"[camera-framing] Camera.main '{camera.name}' is perspective (orthographic=false); " +
                    "CameraFramingPresenter fits the level by setting orthographicSize, which a perspective " +
                    "camera ignores — the level renders UNFRAMED. Set the scene camera to orthographic.");
                return;
            }

            // Framing math lives in Core (CameraFramingMath, TASK-085) so the validator predicts the same orthoSize;
            // the presenter keeps only the Unity contact. Reserve the top PreviewBandFraction for the target preview:
            // content is framed into the lower (1 - band) of the viewport, then the camera is raised by size*band so
            // the top band stays empty for the preview card. TASK-539: the hint button's bottom-right corner is kept
            // clear of the figure and pieces too, with this device's safe area (the button sits inside it). TASK-563:
            // so are the top corners of RESTART and the settings gear.
            var safe = Screen.safeArea;
            var safeLeft = Screen.width > 0 ? safe.xMin / Screen.width : 0f;
            var safeRight = Screen.width > 0 ? (Screen.width - safe.xMax) / Screen.width : 0f;
            var safeBottom = Screen.height > 0 ? safe.yMin / Screen.height : 0f;
            var safeTop = Screen.height > 0 ? (Screen.height - safe.yMax) / Screen.height : 0f;
            if (!CameraFramingMath.TryFrame(level, camera.aspect, _config.Margin, _config.PreviewBandFraction,
                    _config.MinOrthoSize, HudCornerZone.HintButton, safeRight, safeBottom, HudCornerZone.TopButtons,
                    new CameraFramingMath.SafeInsets(safeLeft, safeRight, safeBottom, safeTop),
                    out var size, out var centre))
                return;
            camera.orthographicSize = size;

            var t = camera.transform;
            t.position = new Vector3(centre.X, centre.Y, t.position.z);
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
