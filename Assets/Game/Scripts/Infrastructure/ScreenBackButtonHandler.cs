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
    /// <c>Keyboard.escapeKey</c>; in the editor Escape plays the same role. Unity forum reports that this mapping is not
    /// reliable on every device/Input System version — device behaviour is verified by QA on the phone, not here.
    /// Prod scope only: bringup shows no UiService screens.
    /// </summary>
    [UsedImplicitly]
    public sealed class ScreenBackButtonHandler : ITickable
    {
        private readonly UiService _uiService;

        public ScreenBackButtonHandler(UiService uiService)
        {
            _uiService = uiService;
        }

        public void Tick()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            var top = _uiService.TopPanelName;
            if (string.IsNullOrEmpty(top))
                return;

            if (_uiService.TryGet<IScreenController>(top) is IBackButtonHandler handler)
                handler.OnBackButton();
        }
    }
}
