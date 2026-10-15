using System;
using Game.Services.Input;
using Gameplay.Core;
using JetBrains.Annotations;
using SGS.Services.Ui;
using Ui;
using VContainer.Unity;

namespace Game.Infrastructure
{
    /// <summary>
    /// Modal screen = gameplay input off + simulation stopped. While the settings popup is up this class holds an
    /// input blocker (a tap past the window does not drop a piece) AND a <see cref="GameplayPause"/> token (a piece in
    /// flight does not land behind the popup, TASK-566). Both are taken on ScreenShown and released on ScreenHidden of
    /// the same screen name.
    ///
    /// The restart confirm popup deliberately takes neither (behaviour approved in TASK-153) — see the tech-debt on
    /// TASK-566 for the trigger that changes that.
    /// </summary>
    [UsedImplicitly]
    public sealed class GameplayInputScreenBlocker : IInitializable, IDisposable
    {
        private readonly IInputBlockersHandler _inputBlockersHandler;
        private readonly UiService _uiService;
        private readonly GameplayPause _pause;

        private BlockersToken _token;
        private IDisposable _pauseToken;

        public GameplayInputScreenBlocker(IInputBlockersHandler inputBlockersHandler, UiService uiService, GameplayPause pause)
        {
            _inputBlockersHandler = inputBlockersHandler;
            _uiService = uiService;
            _pause = pause;
        }

        public void Initialize()
        {
            _uiService.ScreenShownEvent += OnScreenShown;
            _uiService.ScreenHiddenEvent += OnScreenHidden;
        }

        public void Dispose()
        {
            if (_uiService != null)
            {
                _uiService.ScreenShownEvent -= OnScreenShown;
                _uiService.ScreenHiddenEvent -= OnScreenHidden;
            }
            Release();
        }

        // Public so EditMode tests can drive it without Addressables (UiService events are not raisable from outside
        // the class) — same seam as HudScreenCoverBridge.
        public void OnScreenShown(string screenName)
        {
            if (screenName != ScreenNames.SettingsScreen)
                return;

            Release();
            _token = _inputBlockersHandler.AddBlockers(InputActionTypeFlags.All);
            _pauseToken = _pause.Acquire();
        }

        public void OnScreenHidden(string screenName)
        {
            if (screenName == ScreenNames.SettingsScreen)
                Release();
        }

        private void Release()
        {
            _token?.Dispose();
            _token = null;
            _pauseToken?.Dispose();
            _pauseToken = null;
        }
    }
}
