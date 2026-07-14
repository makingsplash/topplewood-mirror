using Infrastructure.ApplicationTriggers.Interfaces;

namespace Infrastructure.ApplicationTriggers
{
    public interface IApplicationTriggers : IApplicationPauseTrigger, IApplicationFocusTrigger, IApplicationQuitTrigger { }
}