using System;
using Gameplay.Core;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// TASK-482: editor-only, mouse-only override of <see cref="ShapeFillTuning.CoReleaseWindowSteps"/>. The device
    /// window (4 steps ≈ 67 ms) is sized for two thumbs landing together; a mouse gives ONE press per intent
    /// (<see cref="PointerTapInputSource"/>: no touchscreen → "one press = one intent"), and two clicks inside 67 ms are
    /// not humanly reachable — so in the editor every level with a co-release group was unwinnable by mouse and the
    /// owner judged it by an impossible gesture. With no <see cref="Touchscreen"/> present this widens the window to
    /// <see cref="EditorWindowSteps"/> so the members can be clicked one after the other.
    ///
    /// A TUNING override, not a mechanic branch: <see cref="Gameplay.Core.Mechanic.ShapeFillMechanicController"/> reads
    /// the same knob as on device. Registered only under <c>UNITY_EDITOR</c> (GameplayCoreInstaller), so device and
    /// device-build windows are untouched; the headless sim/sweep bare-news its own tuning (SimRun, LevelSimRunner)
    /// and releases a pair in one step, so it never sees this either. Re-evaluated on every level load: switching the
    /// Device Simulator on/off (which adds/removes a Touchscreen) takes effect on the next level.
    /// </summary>
    public sealed class EditorMouseCoReleaseWindow : IStartable, IDisposable
    {
        /// <summary>≈0.6 s @ 1/60 — wide enough for two deliberate mouse clicks on different pieces.</summary>
        public const int EditorWindowSteps = 36;

        private readonly GameModel _model;
        private readonly ShapeFillTuning _tuning;
        private readonly Func<bool> _hasTouchscreen;
        // The window the tuning shipped with (the device value) — restored whenever a touchscreen is present.
        private readonly int _deviceWindowSteps;
        private IDisposable _levelLoadedSub;

        [Inject]
        public EditorMouseCoReleaseWindow(GameModel model, ShapeFillTuning tuning)
            : this(model, tuning, () => Touchscreen.current != null)
        {
        }

        /// <summary>Test seam: the touchscreen probe is injected so EditMode tests can drive both branches.</summary>
        public EditorMouseCoReleaseWindow(GameModel model, ShapeFillTuning tuning, Func<bool> hasTouchscreen)
        {
            _model = model;
            _tuning = tuning;
            _hasTouchscreen = hasTouchscreen;
            _deviceWindowSteps = tuning.CoReleaseWindowSteps;
        }

        public void Start()
        {
            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => Apply());
            Apply(); // a level may already be loaded by the time entry points start
        }

        /// <summary>Set the window for the current input hardware; log once per level that declares a pair.</summary>
        public void Apply()
        {
            var steps = _hasTouchscreen() ? _deviceWindowSteps : EditorWindowSteps;
            _tuning.CoReleaseWindowSteps = steps;

            var level = _model.CurrentLevel;
            if (steps != _deviceWindowSteps && level != null && level.CoReleaseGroups != null
                && level.CoReleaseGroups.Count > 0)
                Debug.Log($"[co-release] пара: окно редактора {steps} шагов (на устройстве {_deviceWindowSteps}) — " +
                          $"{level.LevelId}, мышь без тачскрина");
        }

        public void Dispose() => _levelLoadedSub?.Dispose();
    }
}
