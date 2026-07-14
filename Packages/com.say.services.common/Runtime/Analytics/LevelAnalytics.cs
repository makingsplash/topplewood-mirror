// using System;
// using System.Collections.Generic;
// using CommonServices;
//
// namespace Services.Analytics
// {
//     
//
//     public class LevelAnalytics : BaseAnalytics
//     {
//         
//         public void LogLevelStarted()
//         {
//             GlobalService.SayKit.TrackLevelStarted(LevelId, LevelNumber);
//         }
//
//         public void LogLevelCompleted()
//         {
//             string context = CollectContextDataString();
//             GlobalService.SayKit.TrackLevelCompleted(LevelId, LevelNumber, 0, context);
//         }
//
//         public void LogLevelFailed(LoseReason loseReason)
//         {
//             Dictionary<string, object> data = new ()
//             {
//                 {"reason", Enum.GetName(typeof(LoseReason), loseReason)?.ToLower()}
//             };
//             string failReason = JsonConverterExtensions.toJson(data);
//             string context = CollectContextDataString();
//             GlobalService.SayKit.TrackLevelFailed(LevelId, LevelNumber, 0,  failReason, context);
//         }
//
//         public void LogWaveStarted()
//         {
//             var data = CollectContextData();
//             data.Add("level_id", LevelId);
//             GlobalService.SayKit.TrackChunkStarted("", 0, data);
//         }
//
//         public void LogWaveCompleted()
//         {
//             var data = CollectContextData();
//             data.Add("level_id", LevelId);
//             GlobalService.SayKit.TrackChunkCompleted(data);
//         }
//
//         public void LogWaveFailed(LoseReason loseReason)
//         {
//             var data = CollectContextData();
//             data.Add("level_id", LevelId);
//             data.Add("reason", Enum.GetName(typeof(LoseReason), loseReason)?.ToLower());
//             GlobalService.SayKit.TrackChunkFailed(data);
//         }
//
//         
//     }
// }