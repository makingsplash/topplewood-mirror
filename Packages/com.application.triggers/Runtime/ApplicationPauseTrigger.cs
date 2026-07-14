using System;
using Infrastructure.ApplicationTriggers.Interfaces;
using UnityEngine;

namespace Infrastructure.ApplicationTriggers
{
    public sealed class ApplicationPauseTrigger : MonoBehaviour, IApplicationPauseTrigger
    {
        public event Action<bool> PauseChanged;
        public bool HasPause { get; private set; }

        private void OnApplicationPause(bool pauseStatus)
        {
            HasPause = pauseStatus;
            PauseChanged?.Invoke(pauseStatus);
        }
    }
}