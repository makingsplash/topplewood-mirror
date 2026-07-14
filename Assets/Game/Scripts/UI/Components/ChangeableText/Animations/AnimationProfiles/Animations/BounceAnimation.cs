using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations
{
    [Serializable]
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    public class BounceAnimation
    {
        [SerializeField] private int _playEverySeconds = 1;
        
        [Space]
        [SerializeField] private Transform _target;
        [SerializeField] private float _bounceInDelay = 0f;
        [SerializeField] private float _bounceInDuration = 0.075f;
        [SerializeField] private float _bounceInStayDuration = 0.01f;
        [SerializeField] private float _bounceOutDuration = 0.075f;

        [SerializeField] private float _bounceInScaleEndValue = 1.15f;
        [SerializeField] private float _defaultScaleValue = 1f;
        
        private Sequence _tween;

        public async UniTask AnimateAsync(Action updateAction)
        {
            if (_tween != null)
            {
                _tween.Kill();
            }
            
            _tween = DOTween.Sequence();
            _ = _tween.SetLink(_target.gameObject, LinkBehaviour.KillOnDestroy);
            _ = _tween.Append(_target.DOScale(_bounceInScaleEndValue, _bounceInDuration).SetDelay(_bounceInDelay).SetEase(Ease.Linear));
            _ = _tween.AppendCallback(() => updateAction?.Invoke());

            if (_bounceInStayDuration > 0f)
            {
                _ = _tween.AppendInterval(_bounceInStayDuration);
            }

            _ = _tween.Append(_target.DOScale(_defaultScaleValue, _bounceOutDuration).SetEase(Ease.Linear));

            await _tween.ToUniTask();
        }

        public void Clear()
        {
            if (_tween != null)
            {
                _tween.Kill();
            }
        }

        public bool IsValidSecond(double currentSeconds)
        {
            return currentSeconds % _playEverySeconds == 0;
        }
    }
}
