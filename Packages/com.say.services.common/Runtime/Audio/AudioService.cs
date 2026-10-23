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

        // TASK-667: the only driver of OnUpdate. Without it a finished source never returns to the pool and
        // SoundEffect.FreeOne is never called, so an effect with _countMax > 0 goes silent for good after _countMax plays.
        private void Update()
        {
            if (_busySoundSources != null)
            {
                OnUpdate();
            }
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

        /// <summary>
        /// TASK-667: start loading the named effects now, so the first play of each does not wait on an Addressables
        /// load (a tap must sound at once). Fire-and-forget; a missing asset is reported once by GetSoundEffect.
        /// </summary>
        public void Preload(IEnumerable<string> effectNames)
        {
            if (_effects == null || effectNames == null)
            {
                return;
            }

            foreach (string effectName in effectNames)
            {
                GetSoundEffect(effectName).Forget();
            }
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

        /// <summary>
        /// TASK-667: play with a per-play volume scale (impact strength) and pitch shift in semitones (the win-star
        /// ladder from one clip). The effect's own pitch jitter is applied on top.
        /// </summary>
        public async UniTask<AudioSource> Play(string effectName, float volumeScale, float semitones)
        {
            return await Play(effectName, false, 0, volumeScale, semitones);
        }

        private async UniTask<AudioSource> Play(string effectName, bool withIndex, int index = 0,
            float volumeScale = 1f, float semitones = 0f)
        {
            SoundEffect effect = await GetSoundEffect(effectName);

            if (effect == null || effect.IsCanPlay() == false)
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
                    effect.Play(source, volumeScale, semitones);
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
            if (config == null || config.MasterMixer == null)
            {
                Debug.LogWarning("[sfx] EnableMusic: no AudioConfig/MasterMixer loaded — the flag is not applied");
                return;
            }
            config.MasterMixer.SetFloat(config.MusicVolumeTag, isEnable ? 0.0f : config.MuteValue);
        }

        public void EnableSounds(bool isEnable)
        {
            AudioConfig config = SayData.Configs.Audio;
            if (config == null || config.MasterMixer == null)
            {
                Debug.LogWarning("[sfx] EnableSounds: no AudioConfig/MasterMixer loaded — the flag is not applied");
                return;
            }
            config.MasterMixer.SetFloat(config.SoundVolumeTag, isEnable ? 0.0f : config.MuteValue);
#if GPIE_DEVELOPMENT
            Debug.Log($"[sfx] EnableSounds({isEnable})");
#endif
        }

        private AudioSource GetSoundAudioSource()
        {
            if (_freeSoundSources.Count > 0)
            {
                return _freeSoundSources.Pop();
            }

            return gameObject.AddComponent<AudioSource>();
        }

        // TASK-667: a load that fails (no asset at that address → InvalidKeyException) used to leave _effects[name] = null
        // forever, and every later Play of that name then waited in WaitWhile for good. Now the failure is reported once
        // and remembered as "missing": later plays of that name return null at once.
        private readonly HashSet<string> _missingEffects = new HashSet<string>();

        private async UniTask<SoundEffect> GetSoundEffect(string effectName)
        {
            if (_missingEffects.Contains(effectName))
            {
                return null;
            }

            if (!_effects.ContainsKey(effectName))
            {
                _effects[effectName] = null;
                SoundEffect effect = null;
                try
                {
                    effect = await Addressables.LoadAssetAsync<SoundEffect>($"SoundEffects/{effectName}.asset");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[sfx] no SoundEffect '{effectName}' (address SoundEffects/{effectName}.asset): {e.Message}");
                }

                if (effect == null)
                {
                    _missingEffects.Add(effectName);
                    _effects.Remove(effectName);
                    return null;
                }

                _effects[effectName] = effect;
                return effect;
            }

            await UniTask.WaitWhile(() => _effects.ContainsKey(effectName) && _effects[effectName] == null);
            return _effects.TryGetValue(effectName, out SoundEffect loaded) ? loaded : null;
        }
    }
}
