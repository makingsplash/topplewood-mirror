using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Services;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Settings
{
    [MovedFrom(sourceNamespace: "UI.Components.Settings")]
    public sealed class SettingsContainer : UIElement
    {
        [SerializeField] private SliderToggle.SliderToggle _soundToggle;
        [SerializeField] private SliderToggle.SliderToggle _musicToggle;
        [SerializeField] private SliderToggle.SliderToggle _hapticToggle;

        protected override UniTask InitializeProcessAsync()
        {
            _soundToggle.OnChangedValue += OnChangedSoundValue;
            _musicToggle.OnChangedValue += OnChangedMusicValue;
            _hapticToggle.OnChangedValue += OnChangedHapticValue;
            
            return base.InitializeProcessAsync();
        }

        protected override UniTask BeforeShowAsync()
        {
            _soundToggle.SetValue(Data.Settings.SoundEnabled.Value, false);
            _musicToggle.SetValue(Data.Settings.MusicEnabled.Value, false);
            _hapticToggle.SetValue(Data.Settings.HapticEnabled.Value, false);
            
            return default;
        }

        private void OnChangedSoundValue(bool newValue)
        {
            Data.Settings.SoundEnabled.Value = newValue;
            GlobalService.AudioService.EnableSounds(newValue);
        }
        
        private void OnChangedMusicValue(bool newValue)
        {
            Data.Settings.MusicEnabled.Value = newValue;
            GlobalService.AudioService.EnableMusic(newValue);
        }
        
        private void OnChangedHapticValue(bool newValue)
        {
            Data.Settings.HapticEnabled.Value = newValue;
            GlobalService.HapticService.EnableHaptic(newValue);
        }
    }
}
