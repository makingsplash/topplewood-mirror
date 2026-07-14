using System.Collections.Generic;

using Newtonsoft.Json;

namespace SGS.Infrastructure.Analytics
{
    public class EventAnalyticsHelper
    {
        public class CohortInfo
        {
            public string CohortNme;
            public int MinLevels;
            public int MaxLevels;
        }

        private readonly IEventAnalyticsBackend _backend;

        public string EventType;
        public string EventName;
        public int EventNo;
        public CohortInfo Cohort;

        public EventAnalyticsHelper(IEventAnalyticsBackend backend)
        {
            _backend = backend;
        }

        public void EventAvailable(Dictionary<string, object> extra = null)
        {
            if (_backend == null)
            {
                return;
            }

            Dictionary<string, object> context = FillContext(extra);
            _backend.TrackEvent(EventAnalyticsNames.EventIsAvailable, context, null);
        }

        public void EventScreenOpen(string screenName, Dictionary<string, object> extra = null)
        {
            if (_backend == null)
            {
                return;
            }

            Dictionary<string, object> context = FillContext(extra);
            _backend.TrackEvent(EventAnalyticsNames.EventScreenOpen, context, screenName);
        }

        public void EventStarted(Dictionary<string, object> extra = null)
        {
            if (_backend == null)
            {
                return;
            }

            Dictionary<string, object> context = FillContext(extra);
            _backend.TrackEvent(EventAnalyticsNames.EventStarted, context, null);
        }

        public void EventMilestone(string milestoneName, int timeSpent, Dictionary<string, object> extra = null,
            Dictionary<string, object> rewardContext = null)
        {
            if (_backend == null)
            {
                return;
            }

            Dictionary<string, object> context = FillContext(extra);
            context.Add("milestone_name", milestoneName);
            context.Add("active_time", timeSpent);

            _backend.TrackEvent(EventAnalyticsNames.EventMilestone, context,
                rewardContext == null ? null : JsonConvert.SerializeObject(rewardContext));
        }

        public void EventResult(string completionType, int timeSpent, int place, int levelsCount,
            Dictionary<string, object> extra = null, Dictionary<string, object> rewardContext = null)
        {
            if (_backend == null)
            {
                return;
            }

            Dictionary<string, object> context = FillContext(extra);
            context.Add("completion_type", completionType);
            context.Add("active_time", timeSpent);
            context.Add("place", place);
            context.Add("lvl_count", levelsCount);

            _backend.TrackEvent(EventAnalyticsNames.EventResult, context,
                rewardContext == null ? null : JsonConvert.SerializeObject(rewardContext));
        }

        private Dictionary<string, object> FillContext(Dictionary<string, object> extra)
        {
            Dictionary<string, object> context = new()
            {
                {"event_type", EventType},
                {"event_name", $"{EventName}_{EventNo}"}
            };
            if (Cohort != null)
            {
                extra.Add("cohort_name", Cohort.CohortNme);
                extra.Add("min_levels", Cohort.MinLevels);
                extra.Add("max_levels", Cohort.MaxLevels);
            }

            if (extra != null)
            {
                foreach (var kv in extra)
                {
                    context.Add(kv.Key, kv.Value);
                }
            }

            return context;
        }
    }
}