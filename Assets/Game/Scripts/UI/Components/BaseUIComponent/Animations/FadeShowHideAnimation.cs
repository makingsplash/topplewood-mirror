using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations
{
    [RequireComponent(typeof(CanvasGroup))]
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    internal sealed class FadeShowHideAnimation : UIShowHideAnimation
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        [SerializeField] private bool _useUnscaledTime;
        
        [Header("Fade in parameters")]
        [SerializeField] private float _fadeInDelay;
        [SerializeField] private float _fadeInDuration = 0.25f;
        [SerializeField] private AnimationCurve _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float _fadeInEndValue = 1f;
        
        [Header("Fade out parameters")]
        [SerializeField] private float _fadeOutDelay;
        [SerializeField] private float _fadeOutDuration = 0.25f;
        [SerializeField] private AnimationCurve _fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float _fadeOutEndValue = 0f;


        public override async UniTask PlayShowAnimationAsync()
        {
            await DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, _fadeInEndValue, _fadeInDuration)
                .SetUpdate(_useUnscaledTime)
                .SetDelay(_fadeInDelay)
                .SetEase(_fadeInCurve)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            await DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, _fadeOutEndValue, _fadeOutDuration)
                .SetUpdate(_useUnscaledTime)
                .SetDelay(_fadeOutDelay)
                .SetEase(_fadeOutCurve)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override void ResetToShown()
        {
            _canvasGroup.alpha = _fadeInEndValue;
        }

        public override void ResetToHidden()
        {
            _canvasGroup.alpha = _fadeOutEndValue;
        }

        private void OnValidate()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }
        }
    }
}
