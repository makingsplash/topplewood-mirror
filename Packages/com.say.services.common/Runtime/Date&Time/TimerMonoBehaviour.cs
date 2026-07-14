using UnityEngine;

namespace SGS.Services.Date_Time
{
    public class TimerMonoBehaviour : MonoBehaviour
    {
        private TimerService _timerService;
        private bool _focused = true;
        private bool _paused = false;

        public void Init(TimerService timerService)
        {
            _timerService = timerService;
        }

        private void Update()
        {
            _timerService.OnUpdate();
        }

        private void LateUpdate()
        {
            _timerService.OnLateUpdate();
        }
        
        private void OnApplicationPause(bool paused)
        {
            _paused = paused;
            _timerService.SetPause(_paused || !_focused);
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            _timerService.SetPause(_paused || !_focused);
        }
    }
}
