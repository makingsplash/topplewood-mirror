using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

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

        // TASK-667: random pitch spread per play, as a fraction (0.06 = ±6 %). 0 = none, the default, so existing assets
        // sound as before. Keeps a repeated one-shot from sounding like a machine gun (design TASK-666 §3.3).
        [Range(0f, 0.2f)]
        [SerializeField]
        private float _pitchJitter;

        // Runtime state of an asset, not data: [NonSerialized] and reset in OnEnable. Without that, in an editor with
        // domain reload off the count survives leaving play mode, and the effect goes silent on the next run.
        [NonSerialized]
        private int _count;

        public bool IsMusic => _isMusic;

        private readonly List<AudioClip> _availableClips = new List<AudioClip>();

        private void OnEnable()
        {
            _count = 0;
            _availableClips.Clear();
        }

        public void Play(AudioSource source)
        {
            Play(source, 1f, 0f);
        }

        public void Play(AudioSource source, int index)
        {
            Play(source, GetAudioClip(index), 1f, 0f);
        }

        /// <summary>
        /// TASK-667: one play with a volume scale and a pitch shift in semitones (+ the asset's jitter on top). Pitch is
        /// written on EVERY play: sources are pooled, so a star's +4 would otherwise leak into the next button click.
        /// </summary>
        public void Play(AudioSource source, float volumeScale, float semitones)
        {
            Play(source, GetAudioClip(), volumeScale, semitones);
        }

        private void Play(AudioSource source, AudioClip clip, float volumeScale, float semitones)
        {
            // Counted even when silent: AudioService frees the (idle) source next frame and calls FreeOne either way.
            _count++;

            if (clip == null)
            {
                return; // an empty bag (clips not delivered yet) plays nothing rather than throwing
            }

            source.clip = clip;
            source.volume = _volume * volumeScale;
            source.pitch = PitchFor(semitones, _pitchJitter > 0f ? Random.Range(-_pitchJitter, _pitchJitter) : 0f);
            source.outputAudioMixerGroup = _mixer;
            source.loop = _isLooped;

            source.Play();
        }

        /// <summary>Playback pitch for a shift in semitones and a jitter fraction: 2^(semitones/12) · (1 + jitter).</summary>
        public static float PitchFor(float semitones, float jitter)
        {
            return Mathf.Pow(2f, semitones / 12f) * (1f + jitter);
        }

        private AudioClip GetAudioClip()
        {
            if (_clips == null || _clips.Length == 0)
            {
                return null;
            }

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
            if (_clips == null || _clips.Length == 0)
            {
                return null;
            }

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
            if (_count > 0)
            {
                _count--;
            }
        }
    }
}
