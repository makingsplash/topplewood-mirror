using System;
using System.Threading;

namespace Common.Extensions
{
    public static class CancellationTokenSourceExtensions
    {
        public static void CancelAndDispose(this CancellationTokenSource cancellationTokenSource)
        {
            if (cancellationTokenSource == null)
                throw new ArgumentNullException(nameof(cancellationTokenSource));

            if (cancellationTokenSource.Token.IsCancellationRequested == false)
                cancellationTokenSource.Cancel();

            cancellationTokenSource.Dispose();
        }

        public static void SafeCancelAndDispose(this CancellationTokenSource cancellationTokenSource)
        {
            if (cancellationTokenSource == null)
                return;

            CancelAndDispose(cancellationTokenSource);
        }
    }
}
