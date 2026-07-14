using System;

namespace Infrastructure.ApplicationTriggers.Interfaces
{
    public interface IApplicationPauseTrigger
    {
        event Action<bool> PauseChanged;
        bool HasPause { get; }
    }
}