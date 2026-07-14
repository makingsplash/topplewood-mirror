using System.Collections.Generic;
using SGS.Services.Interfaces;
using UnityEngine;

namespace SGS.Services.Date_Time
{
    public class TimeScale : IService
    {
        public enum Priority
        {
            None = 0,
            Default = 10,
            GamePause = 10000
        }
        
        private class TimeScaleRequest
        {
            public float TimeScale { get; set; }
            public Priority Priority { get; }
            public int Token { get; }
            
            public TimeScaleRequest(int token, float timeScale, Priority priority)
            {
                Token = token;
                TimeScale = timeScale;
                Priority = priority;
            }
        }

        private readonly float _defaultTimeScale;
        private readonly float _defaultFixedDeltaTime;
        
        private int _nextToken = 1;
        private int? _currentToken;
        private float _currentTimeScale;
        
        private readonly Dictionary<int, TimeScaleRequest> _requests = new Dictionary<int, TimeScaleRequest>();


        public TimeScale()
        {
            _defaultTimeScale = Time.timeScale;
            _defaultFixedDeltaTime = Time.fixedDeltaTime;
            _currentTimeScale = _defaultTimeScale;
        }
        
        public void Init()
        {
            GlobalService.Timer.Update.AddListener(OnUpdate);
        }
        
        public void DeInit()
        {
            Time.timeScale = _defaultTimeScale;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
            
            _requests.Clear();
            _currentToken = null;
            
            GlobalService.Timer.Update.RemoveListener(OnUpdate);
        }

        private void OnUpdate()
        {
            if (_currentToken.HasValue == false)
            {
                SetTimeScaleInner(_defaultTimeScale);
                return;
            }

            TimeScaleRequest currentRequest = _requests[_currentToken.Value];
            SetTimeScaleInner(currentRequest.TimeScale);
        }
        
        public int AddRequest(float timeScale, Priority priority = Priority.Default)
        {
            var request = new TimeScaleRequest(_nextToken, timeScale, priority);
            _nextToken++;
            
            _requests.Add(request.Token, request);

            if (_currentToken.HasValue)
            {
                if (request.Priority > _requests[_currentToken.Value].Priority)
                {
                    _currentToken = request.Token;
                }
            }
            else
            {
                _currentToken = request.Token;
            }

            return request.Token;
        }

        public void RemoveRequest(int token)
        {
            _requests.Remove(token);

            if (_currentToken.HasValue && _currentToken.Value == token)
            {
                _currentToken = null;
                Priority maxPriority = Priority.None;
                foreach (KeyValuePair<int,TimeScaleRequest> pair in _requests)
                {
                    if (pair.Value.Priority > maxPriority)
                    {
                        maxPriority = pair.Value.Priority;
                        _currentToken = pair.Key;
                    }
                }

                if (_currentToken.HasValue == false)
                {
                    Time.timeScale = _defaultTimeScale;
                    Time.fixedDeltaTime = _defaultFixedDeltaTime;
                }
            }
        }

        public void UpdateRequest(int token, float timeScale)
        {
            if (!_requests.ContainsKey(token))
            {
                Debug.LogError("Try to update key that doesn't exist");
                return;
            }
            
            _requests[token].TimeScale = timeScale;
        }

        private void SetTimeScaleInner(float timeScale)
        {
            if (Mathf.Approximately(timeScale, _currentTimeScale))
            {
                return;
            }

            _currentTimeScale = timeScale;
            
            bool isPause = Mathf.Approximately(timeScale, 0f);
            Time.timeScale = isPause ? 0f : timeScale;
            Time.fixedDeltaTime = Mathf.Max( 0.002f,timeScale * _defaultFixedDeltaTime);
        }
    }
}