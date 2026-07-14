using System;
using Common.UsingScope;

namespace Common.Extensions
{
    public static class UsingScopeExtensions
    {
        public static IDisposable GetScope<T>(this T self, Action<T> action, Action<T> disposeAction)
        {
            if (self == null)
                throw new ArgumentNullException(nameof(self));
            
            return new UsingScope<T>(self, action, disposeAction);
        }
    }
}
