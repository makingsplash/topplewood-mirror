using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SGS.Services.Ui;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Drives <see cref="PlayScreenInteraction.IsCoveredByScreen"/> from the UiService screen stack (TASK-398): true while
    /// at least one screen/popup is shown, false once the last one hides. The code-built HUD buttons (RESTART, Hint) sit
    /// on Screen Space Overlay canvases, which always render above the UiService Screen Space Camera canvas regardless
    /// of sortingOrder, so the win/lose dim could never cover them — the presenters hide themselves on this flag instead.
    ///
    /// Tracks shown NAMES, not the top of the stack: a popup closing over the win screen must not uncover the HUD while
    /// the win screen is still up. Same event seam as <c>GameplayInputScreenBlocker</c> (UiService fires
    /// ScreenShown/ScreenHidden from Show/Hide). Prod scope only — bringup raises no UiService screens.
    ///
    /// Known blind spot (UiService, not this class): <c>UiService.DestroyAllScreens</c> releases screens without firing
    /// ScreenHidden. Nothing in the game calls it today; if something starts to, the HUD would stay hidden until the next
    /// show/hide pair.
    /// </summary>
    [UsedImplicitly]
    public sealed class HudScreenCoverBridge : IInitializable, IDisposable
    {
        private readonly UiService _uiService;
        private readonly PlayScreenInteraction _interaction;
        private readonly HashSet<string> _shown = new();

        public HudScreenCoverBridge(UiService uiService, PlayScreenInteraction interaction)
        {
            _uiService = uiService;
            _interaction = interaction;
        }

        public void Initialize()
        {
            // A screen already up when the scope starts: UiService exposes only the top of its stack, so seed with that.
            var top = _uiService.TopPanelName;
            if (!string.IsNullOrEmpty(top))
                OnScreenShown(top);

            _uiService.ScreenShownEvent += OnScreenShown;
            _uiService.ScreenHiddenEvent += OnScreenHidden;
        }

        public void Dispose()
        {
            _uiService.ScreenShownEvent -= OnScreenShown;
            _uiService.ScreenHiddenEvent -= OnScreenHidden;
            _shown.Clear();
            _interaction.IsCoveredByScreen.Value = false;
        }

        // Public so EditMode tests can drive the bookkeeping without Addressables (UiService events are not raisable
        // from outside the class).
        public void OnScreenShown(string screenName)
        {
            _shown.Add(screenName);
            _interaction.IsCoveredByScreen.Value = true;
        }

        public void OnScreenHidden(string screenName)
        {
            _shown.Remove(screenName);
            _interaction.IsCoveredByScreen.Value = _shown.Count > 0;
        }
    }
}
