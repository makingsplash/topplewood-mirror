using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations.Behaviour
{
    [MovedFrom(sourceNamespace: "UI.Components.Animations.Behaviour")]
    public sealed class BounceUIElementAnimation : BehaviourAnimation
    {
        [SerializeField]
        private Transform _target;
        
        [SerializeField]
        private bool _useUnscaledTime = false;
        
        [SerializeField]
        private float _bounceDuration = 0.2f;
        [SerializeField]
        private float _bounceDelay = 0f;
        [SerializeField]
        private float _bouncePeakValue = 1.2f;
        [SerializeField]
        private AnimationCurve _bounceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        
        private Tween _bounceTween;
        private Vector3 _beforeBounceScale;
        
        public async UniTask BounceAsync(Action onPeakBounce, bool force = false)
        {
            if (_bounceTween != null)
            {
                if (force)
                {
                    _bounceTween.Kill();
                    _bounceTween = null;
                }
                else
                {
                    onPeakBounce?.Invoke();
                    return;
                }
            }

            Transform target = _target;
            if (target == null)
            {
                onPeakBounce?.Invoke();
                return;
            }

            _beforeBounceScale = target.localScale;

            if (onPeakBounce != null)
            {
                int tweenSteps = 0;
                _bounceTween = PlayBounceTween(target).OnStepComplete(
                    () =>
                    {
                        tweenSteps++;
                        if (tweenSteps == 1)
                        {
                            onPeakBounce.Invoke();
                        }
                    });
            }
            else
            {
                _bounceTween = PlayBounceTween(target);
            }

            try
            {
                await _bounceTween.ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ResetToBeforeBounce(target);
            ClearBehaviourAnimation();
        }

        private void ResetToBeforeBounce(Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.localScale = _beforeBounceScale;
        }

        private Tween PlayBounceTween(Transform target)
        {
            float duration = _bounceDuration / 2f;
            return target.DOScale(_bouncePeakValue, duration).SetDelay(_bounceDelay)
                .SetLoops(2, LoopType.Yoyo).SetUpdate(_useUnscaledTime).SetEase(_bounceCurve)
                .SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
        }

        protected override void ClearBehaviourAnimation()
        {
            if (_bounceTween != null)
            {
                _bounceTween.Kill();
                _bounceTween = null;
            }
        }

        private void OnValidate()
        {
            if (_target == null)
            {
                _target = transform;
            }
        }
    }
}
