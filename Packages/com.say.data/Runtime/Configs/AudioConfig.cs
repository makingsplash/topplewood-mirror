using SGS.Infrastructure.Tools.Configs;
using UnityEngine;
using UnityEngine.Audio;

namespace SGS.Configs
{
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "Configs/AudioConfig")]
    [GameConfig(ConfigName = "UiPrefabs", ConfigDir = "Configs")]
    public class AudioConfig : ScriptableObject
    {
        [SerializeField]
        private AudioMixer _masterMixer;

        [SerializeField]
        private string _musicVolumeTag;

        [SerializeField]
        private string _soundVolumeTag;

        [SerializeField]
        private float _muteValue;
        
        public AudioMixer MasterMixer => _masterMixer;
        public string MusicVolumeTag => _musicVolumeTag;
        public string SoundVolumeTag=> _soundVolumeTag;
        public float MuteValue => _muteValue;
    }
}