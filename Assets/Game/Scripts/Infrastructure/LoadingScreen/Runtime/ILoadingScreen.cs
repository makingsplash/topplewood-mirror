using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LoadingScreen
{
    public interface ILoadingScreen
    {
        bool IsActive { get; }
        bool IsNotActive { get; }
        float ProgressValue { get; set; }
        IProgress<float> Progress { get; }
        UniTask ShowAsync(bool withAnimation = true, CancellationToken cancellationToken = default);
        UniTask HideAsync(bool withAnimation = true, CancellationToken cancellationToken = default);
    }
}
