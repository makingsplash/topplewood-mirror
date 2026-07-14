using SGS.Infrastructure.Tools.PlayerPrefsWrapper;

namespace SGS.Data
{
    public class SaySettings
    {
        public readonly PlayerPrefsBool MusicEnabled = new PlayerPrefsBool("Settings.MusicEnabled", true);
        public readonly PlayerPrefsBool SoundEnabled = new PlayerPrefsBool("Settings.SoundEnabled", true);
        public readonly PlayerPrefsBool HapticEnabled = new PlayerPrefsBool("Settings.HapticEnabled", true);
    }
}