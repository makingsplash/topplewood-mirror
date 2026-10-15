using System;
using JetBrains.Annotations;
using SGS.Services.Ui;
using Ui.Screens;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace Game.Infrastructure
{
    /// <summary>
    /// One system-"back" handler for every popup (TASK-580, built in TASK-566): back closes the TOP UiService screen if
    /// that screen implements <see cref="IBackButtonHandler"/> (restart → cancel, settings → close), and does nothing
    /// otherwise — win/lose screens wait for their own tap, and with no screen up back is ignored (the app does not quit).
    ///
    /// The project runs the new Input System only (activeInputHandler 1), where Android back arrives as
    /// <c>Keyboard.escapeKey</c>; in the editor Escape plays the same role.
    ///
    /// The press is read from an <see cref="InputAction"/>, NOT from <c>Keyboard.current.escapeKey.wasPressedThisFrame</c>
    /// (TASK-566 QA: the first back after a cold start was lost 2/2 on the phone). The very first
    /// <c>wasPressedThisFrame</c> query of a key judges only the frame's END state (Input System 1.18
    /// <c>ButtonControl.wasPressedThisFrame</c>, "old path"); per-event press tracking starts only after that query. Android
    /// back (adb keyevent, gesture navigation) delivers DOWN and UP in the same frame, so the first press reads as "up" and
    /// is lost; later ones are caught. An action is driven per state event and binds every keyboard, not just
    /// <c>Keyboard.current</c>, so a press that went down and up within one frame still counts.
    /// Prod scope only: bringup shows no UiService screens.
    /// </summary>
    [UsedImplicitly]
    public sealed class ScreenBackButtonHandler : ITickable, IDisposable
    {
        private readonly UiService _uiService;
        private readonly InputAction _back = CreateBackAction();

        public ScreenBackButtonHandler(UiService uiService)
        {
            _uiService = uiService;
            _back.Enable();
        }

        /// <summary>The system-back action (Android back = Escape). Public for the EditMode test of the sub-frame press.</summary>
        public static InputAction CreateBackAction() =>
            new InputAction("ScreenBack", InputActionType.Button, "<Keyboard>/escape");

        public void Tick()
        {
            if (!_back.WasPerformedThisFrame())
                return;

            var top = _uiService.TopPanelName;
            if (string.IsNullOrEmpty(top))
                return;

            if (_uiService.TryGet<IScreenController>(top) is IBackButtonHandler handler)
                handler.OnBackButton();
        }

        public void Dispose() => _back.Dispose();
    }
}
