using System.Collections.Generic;

namespace SGS.Infrastructure.Analytics
{
    public interface IEventAnalyticsBackend
    {
        void TrackEvent(string eventName, Dictionary<string, object> context, string extra2);
    }
}