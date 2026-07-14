using System;
using System.Collections.Generic;

namespace Common.Disposables
{
    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, ICollection<IDisposable> container)
            where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            if (container == null) throw new ArgumentNullException(nameof(container));

            container.Add(disposable);

            return disposable;
        }
    }
}
