using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SGS.Configs;
using SGS.Data;
using SGS.Services.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace SGS.Services.Audio
{
    public class AudioService : MonoBehaviour, IService
    {
        private AudioSource _musicSource;
        private Stack<AudioSource> _freeSoundSources;
        private Queue<AudioSource> _busySoundSources;

        private SoundEffect _currMusic;
        private Dictionary<string, SoundEffect> _effects;

        private static AudioService _instance;

        private readonly Dictionary<AudioSource, SoundEffect> _sourceEffectLink = new Dictionary<AudioSource, SoundEffect>();

        public static AudioService Create()
        {
            if (_instance)
            {
                return _instance;
            }
            
            var audioObject = new GameObject("AudioManager");
            return audioObject.AddComponent<AudioService>();
        }
        
        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
            }
            else
            {
                _instance = this;
            }
        }

        public void Init()
        {
            DontDestroyOnLoad(gameObject);
            
            _freeSoundSources = new Stack<AudioSource>();
            _busySoundSources = new Queue<AudioSource>();
            _effects = new Dictionary<string, SoundEffect>();

            _musicSource = gameObject.AddComponent<AudioSource>();
            _freeSoundSources.Push(gameObject.AddComponent<AudioSource>());

            PreloadSounds();
        }

        public void DeInit()
        {
            
        }

        public void OnUpdate()
        {
            int countBusySources = _busySoundSources.Count;

            for (int i = 0; i < countBusySources; i++)
            {
                AudioSource source = _busySoundSources.Dequeue();
                if (source.isPlaying)
                {
                    _busySoundSources.Enqueue(source);
                }
                else
                {
                    FreeSource(source);
                }
            }
        }

        private void PreloadSounds()
        {
            
        }
        
        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                int countBusySources = _busySoundSources.Count;

                for (int i = 0; i < countBusySources; i++)
                {
                    AudioSource source = _busySoundSources.Dequeue();
                    if (source.isPlaying)
                    {
                        source.Stop();
                        source.clip = null;
                    }
                    
                    FreeSource(source);
                }
            }
        }

        private void FreeSource(AudioSource source)
        {
            if (_sourceEffectLink.ContainsKey(source))
            {
                _sourceEffectLink[source].FreeOne();
                _sourceEffectLink.Remove(source);
            }
            
            _freeSoundSources.Push(source);
        }

        public async UniTask<AudioSource> Play(string effectName)
        {
            return await Play(effectName, false);
        }
        
        public async UniTask<AudioSource> Play(string effectName, int index)
        {
            return await Play(effectName, true, index);
        }
        
        private async UniTask<AudioSource> Play(string effectName, bool withIndex, int index = 0)
        {
            SoundEffect effect = await GetSoundEffect(effectName);

            if (effect.IsCanPlay() == false)
            {
                return null;
            }

            if (effect.IsMusic)
            {
                ChangeMusic(effect);
                return _musicSource;
            }
            else
            {
                AudioSource source = GetSoundAudioSource();
                _sourceEffectLink.Add(source, effect);
                
                if (withIndex)
                {
                    effect.Play(source, index);
                }
                else
                {
                    effect.Play(source);
                }
                
                _busySoundSources.Enqueue(source);
                return source;
            }
        }

        public void Stop(AudioSource source)
        {
            if (source.isPlaying)
            {
                source.Stop();
            }
        }

        private void ChangeMusic(SoundEffect musicEffect)
        {
            if (musicEffect != _currMusic)
            {
                _musicSource.Stop();
                _currMusic = musicEffect;
                if (musicEffect)
                {
                    musicEffect.Play(_musicSource);
                }
            }
        }

        public void EnableMusic(bool isEnable)
        {
            AudioConfig config = SayData.Configs.Audio;
            config.MasterMixer.SetFloat(config.MusicVolumeTag, isEnable ? 0.0f : config.MuteValue);
        }

        public void EnableSounds(bool isEnable)
        {
            AudioConfig config = SayData.Configs.Audio;
            config.MasterMixer.SetFloat(config.SoundVolumeTag, isEnable ? 0.0f : config.MuteValue);
        }

        private AudioSource GetSoundAudioSource()
        {
            if (_freeSoundSources.Count > 0)
            {
                return _freeSoundSources.Pop();
            }

            return gameObject.AddComponent<AudioSource>();
        }

        private async UniTask<SoundEffect> GetSoundEffect(string effectName)
        {
            if (!_effects.ContainsKey(effectName))
            {
                _effects[effectName] = null;
                SoundEffect effect = await Addressables.LoadAssetAsync<SoundEffect>($"SoundEffects/{effectName}.asset");
                _effects[effectName] = effect;
                return effect;
            }

            await UniTask.WaitWhile(() => _effects[effectName] == null);
            return _effects[effectName];
        }
    }
}
