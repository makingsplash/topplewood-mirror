using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class AnalogClock : MonoBehaviour
    {
        [SerializeField] private RectTransform _hoursArrow;
        [SerializeField] private RectTransform _minutesArrow;
        [SerializeField] private RectTransform _secondsArrow;

        [SerializeField] private float _hoursArrowScale = 1;
        [SerializeField] private float _minutesArrowScale = 1;
        [SerializeField] private float _secondsArrowScale = 1;

        [SerializeField] private float _timeScale = 1;
        [SerializeField] private int _accuracy = 1;
        [SerializeField] private float _updateInterval = 0.1f;

        private bool _hasHoursArrow;
        private bool _hasMinutesArrow;
        private bool _hasSecondsArrow;

        private float _lastUpdateTime;

        private void Start()
        {
            _hasHoursArrow = _hoursArrow != null;
            _hasMinutesArrow = _minutesArrow != null;
            _hasSecondsArrow = _secondsArrow != null;

            _lastUpdateTime = float.MinValue;
        }

        private void Update()
        {
            var time = UnityEngine.Time.time;
            if (time - _lastUpdateTime < _updateInterval)
            {
                return;
            }

            _lastUpdateTime = time;

            var elapsedTime = _accuracy > 0
                ? Mathf.Floor(time * _accuracy) / _accuracy
                : time;
            elapsedTime *= _timeScale;

            var axis = Vector3.back;

            if (_hasHoursArrow)
            {
                const float hoursToAngle = 360f / 86400f;
                var angle = elapsedTime * hoursToAngle * _hoursArrowScale;
                _hoursArrow.localRotation = Quaternion.AngleAxis(angle, axis);
            }

            if (_hasMinutesArrow)
            {
                const float minutesToAngle = 360f / 3600f;
                var angle = elapsedTime * minutesToAngle * _minutesArrowScale;
                _minutesArrow.localRotation = Quaternion.AngleAxis(angle, axis);
            }

            if (_hasSecondsArrow)
            {
                const float secondsToAngle = 360f / 60f;
                var angle = elapsedTime * secondsToAngle * _secondsArrowScale;
                _secondsArrow.localRotation = Quaternion.AngleAxis(angle, axis);
            }
        }
    }
}
