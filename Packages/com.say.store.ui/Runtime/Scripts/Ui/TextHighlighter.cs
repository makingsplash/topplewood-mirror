using DG.Tweening;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class TextHighlighter : MonoBehaviour
    {
        [SerializeField] private float _startPosX = -130f;
        [SerializeField] private float _endPosX = 130f;
        [SerializeField] private float _flyDuration = 2f;
        [SerializeField] private float _pauseDuration = 1.6f;
        [SerializeField] private RectTransform _highlighter;
        private Sequence _sequence;

        private void OnEnable()
        {
            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            _sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            _sequence.Append(_highlighter.DOLocalMoveX(_endPosX, _flyDuration));
            _sequence.SetEase(Ease.Linear);
            _sequence.AppendInterval(_pauseDuration);
            _sequence.AppendCallback(() => _highlighter.localPosition = new Vector2(_startPosX, _highlighter.localPosition.y));
            _sequence.SetLoops(-1);
        }

        private void OnDisable()
        {
            _sequence?.Kill();
            _sequence = null;
        }

        private void OnDestroy()
        {
            _sequence?.Kill();
            _sequence = null;
        }
    }
}
