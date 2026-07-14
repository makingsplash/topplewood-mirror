namespace SGS.Services.Analytics
{
    public class ResourceAnalytics
    {
        public void SendSoftIncome(int amount, int total, string place)
        {
            GlobalService.SayKit.TrackSoftIncome(amount, total, place, string.Empty);
        }
        
        public void SendSoftOutcome(int amount, int total, string place)
        {
            GlobalService.SayKit.TrackSoftOutcome(amount, total, place, string.Empty);
        }
    
        public void SendHardIncome(int amount, int total, string place)
        {
            GlobalService.SayKit.TrackHardIncome(amount, total, place, string.Empty);
        }
        
        public void SendHardOutcome(int amount, int total, string place)
        {
            GlobalService.SayKit.TrackHardOutcome(amount, total, place, string.Empty);
        }
    }
}