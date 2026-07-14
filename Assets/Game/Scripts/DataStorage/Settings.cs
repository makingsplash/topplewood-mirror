using SGS.Infrastructure.Tools.PlayerPrefsWrapper;

namespace DataStorage
{
    public enum VisualThemeType
    {
        Light,
        Dark
    }
    public class Settings
    {
        public readonly PlayerPrefsBool MusicEnabled = new("Settings.MusicEnabled", true);
        public readonly PlayerPrefsBool SoundEnabled = new("Settings.SoundEnabled", true);
        public readonly PlayerPrefsBool HapticEnabled = new("Settings.HapticEnabled", true);
        public readonly PlayerPrefsBool VibrationEnabled = new("Settings.VibrationEnabled", true);
        public readonly PlayerPrefsBool ShowCounters = new("Settings.ShowCounters", true);
        public readonly PlayerPrefsBool SendNotifications = new("Settings.SendNotifications", false);

        public readonly PlayerPrefsEnum<VisualThemeType> VisualTheme = new("Settings.VisualTheme", VisualThemeType.Light);
    }
}
