using System;

namespace Infrastructure.ApplicationTriggers.Interfaces
{
    public interface IApplicationQuitTrigger
    {
        event Action IsQuitting;
        event Func<bool> WantsToQuit;
    }
}