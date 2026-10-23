using DataStorage;
using Services;
using VContainer.Unity;

namespace Gameplay.Audio
{
    /// <summary>
    /// Applies the saved «Sound»/«Music» settings to the mixer once per session (TASK-667 plan §3.4). Prod scope only.
    /// From Start, not Awake: <c>AudioMixer.SetFloat</c> called before the mixer is initialised (Awake, or right when
    /// GlobalService creates the AudioService) is silently ignored by Unity, so a saved «off» would not apply. By this
    /// point LoadingGame has also loaded the audio config the service reads. The settings tiles apply later changes
    /// themselves, at once (<c>SettingsPopup</c>).
    /// </summary>
    public sealed class SoundSettingsApplier : IStartable
    {
        public void Start()
        {
            var audio = GlobalService.AudioService;
            if (audio == null)
                return;
            audio.EnableSounds(Data.Settings.SoundEnabled.Value);
            audio.EnableMusic(Data.Settings.MusicEnabled.Value);
        }
    }
}
