namespace SGS.Services.Analytics
{
    public class InfrastructureAnalytics
    {
        public void LogApplicationLoaded()
        {
            GlobalService.SayKit.TrackApplicationLoaded();
        }
    }
}