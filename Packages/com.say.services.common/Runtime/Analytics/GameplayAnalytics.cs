// using System.Collections.Generic;
// using SayGames.Med.Internal;
//
// namespace Services.Analytics
// {
//     public class GameplayAnalytics : BaseAnalytics
//     {
//         public void LogMergeItem(string item1, string item2, string resultItem)
//         {
//             Dictionary<string, object> data = new()
//             {
//                 { "item1", item1 },
//                 { "item2", item2 },
//                 { "result_item", resultItem},
//             };
//             string itemJson = JsonConverterExtensions.toJson(data);
//             string context = CollectContextDataString();
//             //GlobalService.SayKit.TrackTagEvent("merge_units",LevelId, WaveNumber, 0, itemJson, context);
//         }
//     }
// }