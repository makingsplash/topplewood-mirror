using Game.Services.Input;
using JetBrains.Annotations;
using SGS.Services.Ui;
using Ui;
using VContainer.Unity;

namespace Game.Infrastructure
{
    [UsedImplicitly]
    public sealed class GameplayInputScreenBlocker : IInitializable, System.IDisposable
    {
        private readonly IInputBlockersHandler _inputBlockersHandler;
        private readonly UiService _uiService;

        private BlockersToken _token;

        public GameplayInputScreenBlocker(IInputBlockersHandler inputBlockersHandler, UiService uiService)
        {
            _inputBlockersHandler = inputBlockersHandler;
            _uiService = uiService;
        }

        public void Initialize()
        {
            _uiService.ScreenShownEvent += OnScreenShown;
            _uiService.ScreenHiddenEvent += OnScreenHidden;
        }

        public void Dispose()
        {
            _uiService.ScreenShownEvent -= OnScreenShown;
            _uiService.ScreenHiddenEvent -= OnScreenHidden;
            _token?.Dispose();
            _token = null;
        }

        private void OnScreenShown(string screenName)
        {
            if (screenName == ScreenNames.SettingsScreen)
            {
                _token?.Dispose();
                _token = _inputBlockersHandler.AddBlockers(InputActionTypeFlags.All);
            }
        }

        private void OnScreenHidden(string screenName)
        {
            if (screenName == ScreenNames.SettingsScreen)
            {
                _token?.Dispose();
                _token = null;
            }
        }
    }
}
