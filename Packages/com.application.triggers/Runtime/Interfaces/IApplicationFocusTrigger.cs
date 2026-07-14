using System;

namespace Infrastructure.ApplicationTriggers.Interfaces
{
    public interface IApplicationFocusTrigger
    {
        event Action<bool> FocusChanged;
        bool HasFocus { get; }
    }
}