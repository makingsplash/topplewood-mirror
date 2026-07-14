using System;

using UnityEngine;
using UnityEngine.UI;

namespace SGS.Infrastructure.Loading
{
    public class LoadingGameProgress : MonoBehaviour
    {
        public event Action ProgressCompleted;
        
        [SerializeField]
        private Image _progressImage;

        [SerializeField]
        private float _time;

        private float _timer;
        private bool _completed;

        private void Start()
        {
            _progressImage.fillAmount = 0.0f;
            _timer = 0.0f;
            _completed = false;
        }

        private void Update()
        {
            if (_completed)
            {
                return;
            }

            _timer += Time.deltaTime;
            _progressImage.fillAmount = Mathf.Clamp01(_timer / _time);

            if (_timer >= _time)
            {
                _completed = true;
                ProgressCompleted?.Invoke();
            }
        }
    }
}