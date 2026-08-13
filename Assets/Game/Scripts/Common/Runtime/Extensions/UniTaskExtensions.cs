using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Common.Extensions
{
    public static class UniTaskExtensions
    {
        public static UniTask WhenAll(this IEnumerable<UniTask> tasks) =>
            UniTask.WhenAll(tasks);
        
#if UNITY_EDITOR
        // TASK-134: молчание этого цикла превращало один висящий Addressables-загрузчик в вечное «загружается».
        // В редакторе, если за N секунд остались Pending-таски, ГРОМКО (Warning) сообщаем сколько именно — так
        // вечное ожидание перестаёт быть тихим. Идентичность зависшего таска здесь не видна (это анонимные
        // UniTask), её даёт точечная инструментация call-site (см. LoggingAssetLoader в LoadingGame).
        private const double WhenAllStuckWarningSeconds = 15d;
#endif

        public static async UniTask WhenAll(this IEnumerable<UniTask> tasks, IProgress<float> progress, CancellationToken cancellationToken = default)
        {
            using var _ = tasks.ToPooledList(out var allTasks);
            using var __ = allTasks.ToPooledList(out var pendingTasks);

            var allTaskCount = allTasks.Count;

#if UNITY_EDITOR
            var startRealtime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            var stuckWarningEmitted = false;
#endif

            while (pendingTasks.Count > 0)
            {
                var completeTaskCount = allTaskCount - pendingTasks.Count;
                progress.Report((float)completeTaskCount / allTaskCount);

                for (var i = pendingTasks.Count - 1; i >= 0; i--)
                {
                    if (pendingTasks[i].Status != UniTaskStatus.Pending)
                    {
                        pendingTasks.RemoveAt(i);
                    }
                }

#if UNITY_EDITOR
                if (!stuckWarningEmitted
                    && pendingTasks.Count > 0
                    && UnityEngine.Time.realtimeSinceStartupAsDouble - startRealtime >= WhenAllStuckWarningSeconds)
                {
                    stuckWarningEmitted = true;
                    UnityEngine.Debug.LogWarning(
                        $"[UniTaskExtensions.WhenAll] {pendingTasks.Count.ToString()}/{allTaskCount.ToString()} " +
                        $"task(s) still Pending after {WhenAllStuckWarningSeconds.ToString()}s — this await will not " +
                        "complete on its own. Identify the stuck one by the missing completion line in the log " +
                        "(e.g. LoadingGame 'config loaded: <path>' markers).");
                }
#endif

                await UniTask.Yield(cancellationToken);
            }

            progress.Report(1f);
            await UniTask.WhenAll(allTasks);
        }

        public static UniTask.Awaiter GetAwaiter(this int milliseconds) => 
            UniTask.Delay(milliseconds).GetAwaiter();

        public static UniTask.Awaiter GetAwaiter(this float seconds) => 
            UniTask.Delay(TimeSpan.FromSeconds(seconds)).GetAwaiter();

        public static UniTask.Awaiter GetAwaiter(this double seconds) => 
            UniTask.Delay(TimeSpan.FromSeconds(seconds)).GetAwaiter();
    }
}
