using SGS.Services.Analytics;

namespace Services.Analytics
{
    public class UIAnalytics : BaseAnalytics
    {
        public void LogScreen(string screenName)
        {
            GlobalService.SayKit.TrackScreen(screenName);
        }
    }
}
