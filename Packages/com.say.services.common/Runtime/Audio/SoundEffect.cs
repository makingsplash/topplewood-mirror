using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SGS.Services.Audio
{
    [CreateAssetMenu(fileName = "SoundEffect", menuName = "Configs/SoundEffect")]
    public class SoundEffect : ScriptableObject
    {
        [Range(0f, 1f)]
        [SerializeField]
        private float _volume = 1.0f;
        
        [SerializeField]
        private AudioClip[] _clips;
        
        [SerializeField]
        private AudioMixerGroup _mixer;
        
        [SerializeField]
        private bool _isMusic;

        [SerializeField]
        private bool _isLooped;

        [SerializeField]
        private int _countMax;

        private int _count;
        
        public bool IsMusic => _isMusic;

        private readonly List<AudioClip> _availableClips = new List<AudioClip>();

        public void Play(AudioSource source)
        {
            Play(source, GetAudioClip());
        }
        
        public void Play(AudioSource source, int index)
        {
            Play(source, GetAudioClip(index));
        }

        private void Play(AudioSource source, AudioClip clip)
        {
            _count++;
            
            source.clip = clip;
            source.volume = _volume;
            source.outputAudioMixerGroup = _mixer;
            source.loop = _isLooped;
            
            source.Play();
        }

        private AudioClip GetAudioClip()
        {
            if (_availableClips.Count == 0)
            {
                _availableClips.AddRange(_clips);
            }

            int randomClipIndex = Random.Range(0, _availableClips.Count);
            AudioClip selectedClip = _availableClips[randomClipIndex];
            _availableClips.RemoveAt(randomClipIndex);
            return selectedClip;
        }
        
        private AudioClip GetAudioClip(int index)
        {
            if (index >= _clips.Length)
            {
                index = _clips.Length - 1;
            }
            
            return _clips[index];
        }

        public bool IsCanPlay()
        {
            return _countMax == 0 || _count < _countMax;
        }

        public void FreeOne()
        {
            _count--;
        }
    }
}