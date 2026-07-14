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
        
        public static async UniTask WhenAll(this IEnumerable<UniTask> tasks, IProgress<float> progress, CancellationToken cancellationToken = default)
        {
            using var _ = tasks.ToPooledList(out var allTasks);
            using var __ = allTasks.ToPooledList(out var pendingTasks);

            var allTaskCount = allTasks.Count;

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
