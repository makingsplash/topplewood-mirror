using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations
{
    [Serializable]
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    public class HighlightAnimation
    {
        [SerializeField] private int _playEverySeconds = 1;
        
        [Space]
        [SerializeField] private MaskableGraphic _target;
        [SerializeField] private float _colorBounceInDelay = 0f;
        [SerializeField] private float _colorBounceInDuration = 0.075f;
        [SerializeField] private float _colorBounceInStayDuration = 0.01f;
        [SerializeField] private float _colorBounceOutDuration = 0.075f;

        [SerializeField] private Color _colorBounceInEndValue = Color.red;
        [SerializeField] private Color _defaultColorValue = Color.white;

        private Sequence _tween;
        
        public async UniTask AnimateAsync(Action updateAction)
        {
            if (_tween != null)
            {
                _tween.Kill();
            }
            
            _tween = DOTween.Sequence();
            _ = _tween.SetLink(_target.gameObject, LinkBehaviour.KillOnDestroy);
            _ = _tween.Append(DOTween.To(() => _target.color,    
                x => {
                    _target.color = x;
                },                      
                _colorBounceInEndValue,               
                _colorBounceInDuration                      
            ).SetDelay(_colorBounceInDelay).SetEase(Ease.Linear));
            _ = _tween.AppendCallback(() => updateAction?.Invoke());
            
            if (_colorBounceInStayDuration > 0f)
            {
                _ = _tween.AppendInterval(_colorBounceInStayDuration);
            }

            _ = _tween.Append(DOTween.To(() => _target.color,    
                x => {
                    _target.color = x;
                },                      
                _defaultColorValue,               
                _colorBounceOutDuration                   
            ).SetEase(Ease.Linear));

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
