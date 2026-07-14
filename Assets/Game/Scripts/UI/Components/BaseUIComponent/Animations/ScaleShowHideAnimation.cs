using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations
{
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    internal sealed class ScaleShowHideAnimation : UIShowHideAnimation
    {
        [SerializeField] private Transform _target;

        [SerializeField] private bool _useUnscaledTime = false;
        
        [Header("ScaleIn parameters")]
        [SerializeField] private float _scaleInDelay = 0f;
        [SerializeField] private float _scaleInDuration = 0.25f;
        [SerializeField] private AnimationCurve _scaleInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float _scaleInEndValue = 1f;
        
        [Header("ScaleOut parameters")]
        [SerializeField] private float _scaleOutDelay = 0f;
        [SerializeField] private float _scaleOutDuration = 0.25f;
        [SerializeField] private AnimationCurve _scaleOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float _scaleOutEndValue = 0f;
        
        public override async UniTask PlayShowAnimationAsync()
        {
            if (_target == null)
            {
                return;
            }

            await _target.DOScale(_scaleInEndValue, _scaleInDuration)
                .SetUpdate(_useUnscaledTime)
                .SetDelay(_scaleInDelay)
                .SetEase(_scaleInCurve)
                .SetLink(_target.gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            if (_target == null)
            {
                return;
            }

            await _target.DOScale(_scaleOutEndValue, _scaleOutDuration)
                .SetUpdate(_useUnscaledTime)
                .SetDelay(_scaleOutDelay)
                .SetEase(_scaleOutCurve)
                .SetLink(_target.gameObject, LinkBehaviour.KillOnDestroy)
                .SetAutoKill()
                .ToUniTask();
        }

        public override void ResetToShown()
        {
            if (_target == null)
            {
                return;
            }

            _target.localScale = Vector3.one * _scaleInEndValue;
        }

        public override void ResetToHidden()
        {
            if (_target == null)
            {
                return;
            }

            _target.localScale = Vector3.one * _scaleOutEndValue;
        }

        private void Awake()
        {
            if (_target == null)
            {
                Debug.LogError(
                    $"[{nameof(ScaleShowHideAnimation)}] Missing target on object '{gameObject.name}' in scene '{gameObject.scene.name}'.",
                    this);
            }
        }

#if UNITY_EDITOR
        private void OnValidate() { }
#endif
    }
}
