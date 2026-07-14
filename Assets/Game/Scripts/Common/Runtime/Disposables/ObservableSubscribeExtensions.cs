#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using R3;

public static class ObservableSubscribeExtensions
{
    [DebuggerStepThrough]
    public static IDisposable SubscribeOnce<T>(
        this Observable<T> source,
        Action<T> onNext)
    {
        IDisposable? subscription = null;
        int once = 0;

        subscription = source.Subscribe(x =>
        {
            if (Interlocked.Exchange(ref once, 1) != 0)
            {
                return;
            }

            subscription?.Dispose();

            onNext(x);
        });

        return subscription!;
    }
}
