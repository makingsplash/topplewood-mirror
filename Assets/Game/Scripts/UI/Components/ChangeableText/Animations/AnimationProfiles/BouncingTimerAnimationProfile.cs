using System;
using Cysharp.Threading.Tasks;
using Ui.Components.Animations;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public sealed class BouncingTimerAnimationProfile : TimerAnimationProfile
    {
        [SerializeField] private BounceAnimation _animation;


        public override async UniTask AnimateAsync(TimeSpan currentTime, Action updateAction)
        {
            if (_animation.IsValidSecond(currentTime.TotalSeconds))
            {
                await _animation.AnimateAsync(updateAction);
            }
            else
            {
                updateAction?.Invoke();
            }
        }

        public override async UniTask AnimateAsync(Action updateAction)
        {
            await _animation.AnimateAsync(updateAction);
        }

        protected override void Clear()
        {
            _animation.Clear();
        }
    }
}
