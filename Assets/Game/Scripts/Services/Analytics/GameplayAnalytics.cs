using SGS.Services.Analytics;

namespace Services.Analytics
{
    public class GameplayAnalytics : BaseAnalytics
    {
        /*
        public void LogMergeItem(string item1, string item2, string resultItem)
        {
            Dictionary<string, object> data = new()
            {
                { "item1", item1 },
                { "item2", item2 },
                { "result_item", resultItem},
            };
            string itemJson = JsonConverterExtensions.toJson(data);
            string context = CollectContextDataString();
            GlobalService.SayKit.TrackTagEvent("merge_units",LevelId, WaveNumber, 0, itemJson, context);
        }
        
        public void LogRefreshUnits(RefreshItemsSource source)
        {
            Dictionary<string, object> data = new ()
            {
                {"source", Enum.GetName(typeof(RefreshItemsSource), source)?.ToLower()}
            };
            string sourceJson = JsonConverterExtensions.toJson(data);
            string context = CollectContextDataString();
            GlobalService.SayKit.TrackTagEvent("refresh_units",LevelId, WaveNumber, 0, sourceJson, context);
        }
        */
    }
}
