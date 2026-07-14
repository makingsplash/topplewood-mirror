using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SGS.Extensions;
using SGS.UnityExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace  SGS.Infrastructure.Loading
{
    public class SceneLoader : MonoBehaviour
    {
        [SerializeField] 
        private Image _fadeImage;
        [SerializeField] 
        private Slider _progressSlider;
        [SerializeField] 
        private TMP_Text _progressLabel;

        private int _allTaskCount;
        private int _completeTaskCount;
        private float _dump;
        private float _progress;
        private float _progressView;
        private float _lastProgressChangeTime;

        [Preserve]
        public static async UniTask Load(string sceneName, UniTask[] loadTasks = null, float fadeTime = 0.4f, float dump = 0.2f, Action onLoadTaskComplete = null)
        {
            var loaderObject = await Addressables.InstantiateAsync("SceneLoader");
            SceneLoader sceneLoader = loaderObject.GetComponent<SceneLoader>();
            sceneLoader.Init(dump);

            await sceneLoader.FadeInOut(fadeTime);
            await sceneLoader.LoadTasks(loadTasks);
            onLoadTaskComplete?.Invoke();
            await sceneLoader.LoadScene(sceneName);
            await sceneLoader.FadeInOut(fadeTime);
        }

        private void Init(float dump)
        {
            _dump = dump;
            _progressSlider.value = 0;
            _progressLabel.text = "0%";
            DontDestroyOnLoad(this);
        }
        
        private async UniTask FadeInOut(float fadeTime)
        {
            if (fadeTime == 0)
            {
                return;
            }
            
            float duration = fadeTime * 0.5f;
            _fadeImage.color.SetAlpha(0);
            float time = 0;
            while (time < duration)
            {
                _fadeImage.color.SetAlpha(Mathf.Lerp(0, 1, time / duration));
                time += Time.deltaTime;
                await UniTask.Yield();
            }
            
            time = 0;
            while (time < duration)
            {
                _fadeImage.color.SetAlpha(Mathf.Lerp(1, 0, time / duration));
                time += Time.deltaTime;
                await UniTask.Yield();
            }
        }

        private async UniTask LoadTasks(UniTask[] loadTasks = null)
        {
            _allTaskCount = 1;
            _completeTaskCount = 0;
            int lastCompleteCount = 0;
            if (loadTasks == null || loadTasks.Length == 0)
            {
                return;
            }

            _allTaskCount += loadTasks.Length;

            List<UniTask> tasks = new List<UniTask>(loadTasks);
            while (tasks.Count > 0)
            {
                _completeTaskCount = _allTaskCount - tasks.Count;
                if (lastCompleteCount != _completeTaskCount)
                {
                    lastCompleteCount = _completeTaskCount;
                    _lastProgressChangeTime = Time.time;
                }
                UpdateProgress((float)_completeTaskCount / _allTaskCount);
                
                tasks.RemoveAll(t => t.Status != UniTaskStatus.Pending);
                await UniTask.Yield();
            }
        }
        
        private async UniTask LoadScene(string sceneName)
        {
            await Addressables.LoadSceneAsync($"Scenes/{sceneName}.unity");
        }

        private void UpdateProgress(float value)
        {
            _progress = value;
            
            if (_progress.Aproximate(_progressView))
            {
                return;
            }

            float delta = (Time.time - _lastProgressChangeTime) / _dump;
            _progressView = Mathf.Lerp(_progressView, _progress, delta);
            _progressSlider.value = _progressView;
            int percent = (int)(100 * _progressView);
            _progressLabel.text = $"{percent}%";
        }

        [Preserve]
        public async void SetProgress(float value)
        {
            if (_progress < value)
            {
                float changeTime = Time.time;
                _lastProgressChangeTime = changeTime;
                while (_lastProgressChangeTime + _dump > Time.time)
                {
                    UpdateProgress(value);
                    await UniTask.Yield();
                    if (changeTime != _lastProgressChangeTime)
                    {
                        return;
                    }
                }
            }
        }

        [Preserve]
        public void Close()
        {
            Addressables.Release(gameObject);
        }
    }
}