using System;
using SGS.Services.Interfaces;

namespace SGS.Services.Gameplay
{
    public class PauseService : IService
    {
        private int _pauseCounter;
        
        public bool IsPaused => _pauseCounter > 0;

        public event Action OnPause;
        public event Action OnResume;

        public void Init()
        {
            
        }

        public void DeInit()
        {
            
        }

        public void ResetPause()
        {
            bool wasPaused = IsPaused;
            _pauseCounter = 0;
            if (wasPaused)
            {
                OnResume?.Invoke();
            }
        }

        public void PauseRequest()
        {
            _pauseCounter++;
            OnPause?.Invoke();
        }

        public void UnpauseRequest()
        {
            if (IsPaused)
            {
                _pauseCounter--;
                OnResume?.Invoke();
            }
        }
    }
}