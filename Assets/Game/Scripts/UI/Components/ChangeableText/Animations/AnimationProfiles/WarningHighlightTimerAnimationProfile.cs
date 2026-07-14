using System;
using Cysharp.Threading.Tasks;
using Services;
using Ui.Components.Animations;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public sealed class WarningHighlightTimerAnimationProfile : TimerAnimationProfile
    {
        [SerializeField] private string _sound;
        [SerializeField] private BounceAnimation _bounceAnimation;
        [SerializeField] private HighlightAnimation _highlightAnimation;

        private AudioSource _audioSource;


        public override async UniTask AnimateAsync(TimeSpan currentTime, Action updateAction)
        {
            bool hasValidSecond = _bounceAnimation.IsValidSecond(currentTime.TotalSeconds) || _highlightAnimation.IsValidSecond(currentTime.TotalSeconds);
            if (hasValidSecond)
            {
                PlaySound();
                
                UniTask bounce = _bounceAnimation.IsValidSecond(currentTime.TotalSeconds) ? _bounceAnimation.AnimateAsync(updateAction) : default;
                UniTask highlight = _highlightAnimation.IsValidSecond(currentTime.TotalSeconds)
                    ? _highlightAnimation.AnimateAsync(null)
                    : default;
            
                await UniTask.WhenAll(bounce, highlight);
            }
            else
            {
                StopSound();
                updateAction?.Invoke();
            }
        }

        public override async UniTask AnimateAsync(Action updateAction)
        {
            UniTask bounce = _bounceAnimation.AnimateAsync(updateAction);
            UniTask highlight = _highlightAnimation.AnimateAsync(null);
            
            await UniTask.WhenAll(bounce, highlight);
        }

        private void StopSound()
        {
            if (_audioSource)
            {
                GlobalService.AudioService.Stop(_audioSource);
            }
        }
        
        private async void PlaySound()
        {
            if (!_audioSource && !string.IsNullOrEmpty(_sound))
            {
                _audioSource = await GlobalService.AudioService.Play(_sound);
            }
        }

        protected override void Clear()
        {
            if (_audioSource)
            {
                GlobalService.AudioService.Stop(_audioSource);
                _audioSource = null;
            }
            
            _bounceAnimation.Clear();
            _highlightAnimation.Clear();
        }
    }
}
