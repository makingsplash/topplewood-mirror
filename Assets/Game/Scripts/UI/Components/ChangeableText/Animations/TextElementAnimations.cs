using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public sealed class TextElementAnimations : UIShowHideAnimation
    {
        [SerializeField] private UIShowHideAnimation _showHideAnimation;
        [SerializeField] private TimerAnimationProfile _baseAnimationProfile;
        [SerializeField] private TimerAnimationProfile[] _animationProfiles;


        public async UniTask UpdateValueAnimationForTimersAsync(TimeSpan currentTime, Action updateAction)
        {
            foreach (TimerAnimationProfile profile in _animationProfiles)
            {
                if (profile.IsAvailable(currentTime.TotalSeconds))
                {
                    await profile.AnimateAsync(currentTime, updateAction);
                    return;
                }
            }
            
            updateAction?.Invoke();
        }

        public async UniTask UpdateValueAnimationAsync(Action updateAction)
        {
            if (_baseAnimationProfile == null)
            {
                updateAction?.Invoke();
            }
            else
            {
                await _baseAnimationProfile.AnimateAsync(updateAction);
            }
        }

        public override async UniTask PlayShowAnimationAsync()
        {
            if (_showHideAnimation == null)
            {
                return;
            }
            
            await _showHideAnimation.PlayShowAnimationAsync();
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            if (_showHideAnimation == null)
            {
                return;
            }

            await _showHideAnimation.PlayHideAnimationAsync();
        }

        public override void ResetToShown()
        {
            if (_showHideAnimation == null)
            {
                return;
            }

            _showHideAnimation.ResetToShown();
        }

        public override void ResetToHidden()
        {
            if (_showHideAnimation == null)
            {
                return;
            }

            _showHideAnimation.ResetToHidden();
        }
    }
}
