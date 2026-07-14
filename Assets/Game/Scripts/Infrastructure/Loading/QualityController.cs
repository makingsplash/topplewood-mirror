using UnityEngine;

public class QualityController : MonoBehaviour
{
    [SerializeField] private float _checkInterval = 1f;
    [SerializeField] private float _targetFPS = 30f;
    [SerializeField] private float _lowFPSTimeThreshold = 5f;  

    private float _timer;
    private float _lowFPSTimer;
    private bool _isEnabled = true;

    private void Update()
    {
        if (!_isEnabled)
        {
            return;
        }

        _timer += Time.unscaledDeltaTime;
        if (_timer < _checkInterval)
        {
            return;
        }

        var currentFPS = 1f / Time.unscaledDeltaTime;
        _timer = 0f;

        if (currentFPS < _targetFPS)
        {
            _lowFPSTimer += _checkInterval;
            if (_lowFPSTimer < _lowFPSTimeThreshold)
            {
                return;
            }
            
            TryDecreaseQuality();
            _lowFPSTimer = 0f;
        }
        else
        {
            _lowFPSTimer = 0f;
        }
    }

    private void TryDecreaseQuality()
    {
        QualitySettings.DecreaseLevel(true);
        if (QualitySettings.GetQualityLevel() == 0)
        {
            _isEnabled = false;
        }
    }
}
