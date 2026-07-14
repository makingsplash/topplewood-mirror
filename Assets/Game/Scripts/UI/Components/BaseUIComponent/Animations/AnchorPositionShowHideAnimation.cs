using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations
{
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    internal sealed class AnchorPositionShowHideAnimation : UIShowHideAnimation
    {
        [SerializeField] private RectTransform _target;

        [Header("InAnimation  parameters")]
        [SerializeField] private float _inDelay;
        [SerializeField] private float _inDuration = 0.25f;
        [SerializeField] private AnimationCurve _inCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private Vector2 _inEndValue; 
        
        [Header("OutAnimation parameters")]
        [SerializeField] private float _outDelay;
        [SerializeField] private float _outDuration = 0.25f;
        [SerializeField] private AnimationCurve _outCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private Vector2 _outEndValue; 
        
        public override async UniTask PlayShowAnimationAsync()
        {
            await DOTween.To(() => _target.anchoredPosition, x => _target.anchoredPosition = x, _inEndValue, _inDuration)
                .SetDelay(_inDelay)
                .SetEase(_inCurve)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            await DOTween.To(() => _target.anchoredPosition, x => _target.anchoredPosition = x, _outEndValue, _outDuration)
                .SetDelay(_outDelay)
                .SetEase(_outCurve)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override void ResetToShown()
        {
            _target.anchoredPosition = _inEndValue;
        }

        public override void ResetToHidden()
        {
            _target.anchoredPosition = _outEndValue;
        }

#if UNITY_EDITOR
        [Sirenix.OdinInspector.Button]
        private void BakeInPosition()
        {
            _inEndValue = _target.anchoredPosition;
        }

        [Sirenix.OdinInspector.Button]
        private void BakeOutPosition()
        {
            _outEndValue = _target.anchoredPosition;
        }
#endif
    }
}
