using System;
using System.Threading;
using Common.UsingScope;
using Cysharp.Threading.Tasks;

namespace LoadingScreen
{
    public static class LoadingScreenExtensions
    {
        public static IDisposable ShowScope(
            this ILoadingScreen loadingScreen,
            bool withAnimation = true,
            CancellationToken cancellationToken = default)
        {
            return new UsingScope<ILoadingScreen>(
                loadingScreen,
                screen => screen.ShowIfNotActiveAsync(withAnimation, cancellationToken),
                screen => screen.HideIfActiveAsync(withAnimation, cancellationToken));
        }
        
        public static UniTask ShowIfNotActiveAsync(
            this ILoadingScreen loadingScreen,
            bool withAnimation = true,
            CancellationToken cancellationToken = default)
        {
            if (loadingScreen == null)
                throw new ArgumentNullException(nameof(loadingScreen));

            return loadingScreen.IsActive 
                ? UniTask.CompletedTask 
                : loadingScreen.ShowAsync(withAnimation, cancellationToken);
        }

        public static UniTask HideIfActiveAsync(
            this ILoadingScreen loadingScreen,
            bool withAnimation = true,
            CancellationToken cancellationToken = default)
        {
            if (loadingScreen == null)
                throw new ArgumentNullException(nameof(loadingScreen));
            
            return loadingScreen.IsNotActive 
                ? UniTask.CompletedTask 
                : loadingScreen.HideAsync(withAnimation, cancellationToken);
        }
    }
}
