using DG.Tweening;
using TMPro;
using UnityEngine;
using VContainer;
using Game.Services.LogService;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class ButtonTextColorAnimator : MonoBehaviour
    {

        [Header("References")]
        [SerializeField] private TMP_Text _targetText;

        [Header("Colors")]
        [SerializeField] private Color _initialColor = Color.white;
        [SerializeField] private Color _clickColor = Color.gray;

        [Header("Animation")]
        [SerializeField] private float _animationDuration = 0.15f;
        [SerializeField] private Ease _forwardEase = Ease.OutQuad;
        [SerializeField] private Ease _backwardEase = Ease.InQuad;

        private Sequence _sequence;
        private ILogService _logService;

        [Inject]
        public void Construct(ILogService logService)
        {
            _logService = logService;
        }

        private void Awake()
        {
            if (_targetText == null)
            {
                _logService?.Log(LogLevel.Error, $"{nameof(ButtonTextColorAnimator)}: TargetText is not assigned.", LogCategory.UI, this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            KillAnimation();

            if (_targetText != null)
            {
                _targetText.color = _initialColor;
            }
        }

        /// <summary>
        /// Этот метод привязывается в Button.onClick
        /// </summary>
        public void PlayClickAnimation()
        {
            if (_targetText == null)
                return;

            KillAnimation();

            _targetText.color = _initialColor;

            _sequence = DOTween.Sequence()
                .SetLink(gameObject)
                .Append(_targetText.DOColor(_clickColor, _animationDuration).SetEase(_forwardEase))
                .Append(_targetText.DOColor(_initialColor, _animationDuration).SetEase(_backwardEase));
        }

        private void OnDisable()
        {
            KillAnimation();

            if (_targetText != null)
            {
                _targetText.color = _initialColor;
            }
        }

        private void OnDestroy()
        {
            KillAnimation();
        }

        private void KillAnimation()
        {
            if (_sequence != null && _sequence.IsActive())
            {
                _sequence.Kill();
                _sequence = null;
            }
        }
    }
}
