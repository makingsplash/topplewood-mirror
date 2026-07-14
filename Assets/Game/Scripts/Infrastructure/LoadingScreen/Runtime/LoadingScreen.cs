using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace LoadingScreen
{
    public sealed class LoadingScreen : MonoBehaviour, ILoadingScreen
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private CanvasGroup _canvasGroup;
        [Header("Common")] [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.Linear;

        [Header("Progress bar")] [SerializeField]
        private Slider _progressBar;

        [SerializeField] private float _progressAnimationDuration = 0.3f;
        [SerializeField] private Ease _progressAnimationEase = Ease.OutQuint;

        private readonly Progress<float> _progress = new();

        private Tween _tween;
        private Tween _progressTween;

        public bool IsActive { get; private set; }
        public bool IsNotActive => !IsActive;

        public float ProgressValue
        {
            get => _progressBar != null ? _progressBar.normalizedValue : 0f;
            set => ProgressAnimation(Mathf.Clamp01(value));
        }

        public IProgress<float> Progress => _progress;

        public void Awake() => _progress.ProgressChanged += OnProgressChanged;

        public void Start()
        {
            if (_root != null)
            {
                _root.SetActive(IsActive);
            }
        }

        public void OnDestroy()
        {
            _progress.ProgressChanged -= OnProgressChanged;
            _tween?.Kill();
            _progressTween?.Kill();
            _tween = null;
            _progressTween = null;
        }

        private void OnProgressChanged(object sender, float value) => ProgressAnimation(Mathf.Clamp01(value));

        public async UniTask ShowAsync(bool withAnimation, CancellationToken cancellationToken)
        {
            IsActive = true;
            await FadeInAsync(withAnimation, cancellationToken);
        }

        public async UniTask HideAsync(bool withAnimation, CancellationToken cancellationToken)
        {
            IsActive = false;
            await FadeOutAsync(withAnimation, cancellationToken);
        }

        private void ProgressAnimation(float progress)
        {
            _progressTween?.Kill();
            _progressTween = null;

            if (_progressBar == null)
                return;

            _progressTween = DOVirtual
                .Float(_progressBar.normalizedValue, progress, _progressAnimationDuration, value => _progressBar.normalizedValue = value)
                .SetUpdate(true)
                .SetEase(_progressAnimationEase)
                .SetAutoKill(true)
                .Play();
        }

        private UniTask FadeInAsync(bool withAnimation = true, CancellationToken cancellationToken = default)
        {
            if (_root == null || _canvasGroup == null)
            {
                return UniTask.CompletedTask;
            }

            _root.SetActive(true);
            _canvasGroup.alpha = withAnimation ? 0f : 1f;

            return GetFadeTween(1f, withAnimation ? _duration : 0f)
                .ToUniTask(TweenCancelBehaviour.CompleteAndCancelAwait, cancellationToken);
        }

        private UniTask FadeOutAsync(bool withAnimation = true, CancellationToken cancellationToken = default)
        {
            if (_root == null || _canvasGroup == null)
            {
                return UniTask.CompletedTask;
            }

            return GetFadeTween(0f, withAnimation ? _duration : 0f)
                .OnComplete(() =>
                {
                    if (_root != null)
                    {
                        _root.SetActive(false);
                    }

                    _progressTween?.Kill();
                    if (_progressBar != null)
                    {
                        _progressBar.normalizedValue = 0;
                    }
                })
                .ToUniTask(TweenCancelBehaviour.CompleteAndCancelAwait, cancellationToken);
        }

        private Tween GetFadeTween(float to, float duration)
        {
            _tween?.Kill();

            if (_canvasGroup == null)
            {
                _tween = DOVirtual.DelayedCall(0f, () => { }).SetUpdate(true).SetAutoKill(true).Play();
                return _tween;
            }

            _tween = _canvasGroup
                .DOFade(to, duration)
                .From(1f - to)
                .SetLink(gameObject)
                .SetUpdate(true)
                .SetEase(_ease)
                .SetAutoKill(true)
                .Play();

            return _tween;
        }
    }
}
