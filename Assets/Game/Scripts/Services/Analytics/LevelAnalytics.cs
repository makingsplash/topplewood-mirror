using System.Collections.Generic;
using Newtonsoft.Json;
using SGS.Infrastructure.Core;
using SGS.Services.Analytics;

namespace Services.Analytics
{
    public class LevelAnalytics : BaseAnalytics
    {
        public void LogLevelStarted()
        {
            GlobalService.SayKit.TrackLevelStarted(LevelId, LevelNumber, "", "");
        }

        public void LogLevelCompleted(Dictionary<string, object> levelStats)
        {
            string extra = SerializeStats(levelStats);
            GlobalService.SayKit.TrackLevelCompleted(LevelId, LevelNumber, extra, "");
        }

        public void LogLevelFailed(string reason, Dictionary<string, object> levelStats = null)
        {
            var data = new Dictionary<string, object>
            {
                { "reason", reason }
            };
            if (levelStats != null)
            {
                foreach (var kvp in levelStats)
                    data[kvp.Key] = kvp.Value;
            }
            string extra = JsonConvert.SerializeObject(data);
            GlobalService.SayKit.TrackLevelFailed(LevelId, LevelNumber, extra, "");
        }

        private static string SerializeStats(Dictionary<string, object> levelStats)
        {
            return levelStats == null || levelStats.Count == 0
                ? ""
                : JsonConvert.SerializeObject(levelStats);
        }
    }
}
