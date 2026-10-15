namespace Ui.Screens
{
    /// <summary>
    /// A screen that knows what the system "back" means for it (TASK-580, built in TASK-566). <c>ScreenBackButtonHandler</c>
    /// forwards back to the TOP UiService screen only if it implements this — a dismissable popup closes itself the
    /// way its own ✕ does; a screen without it (win/lose, which wait for their own tap) ignores back.
    /// </summary>
    public interface IBackButtonHandler
    {
        void OnBackButton();
    }
}
