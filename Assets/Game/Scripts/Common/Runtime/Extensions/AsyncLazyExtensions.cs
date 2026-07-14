using Cysharp.Threading.Tasks;

namespace Common.Extensions
{
    public static class AsyncLazyExtensions
    {
        public static bool IsCompleted<T>(this AsyncLazy<T> asyncLazy)
        {
            return asyncLazy?.Task.Status.IsCompleted() ?? true;
        }

        public static bool IsCompleted(this AsyncLazy asyncLazy)
        {
            return asyncLazy?.Task.Status.IsCompleted() ?? true;
        }

        public static bool IsNotCompleted<T>(this AsyncLazy<T> asyncLazy) =>
            IsCompleted(asyncLazy) == false;

        public static bool IsNotCompleted(this AsyncLazy asyncLazy)
            => IsCompleted(asyncLazy) == false;
    }
}
