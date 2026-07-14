using System;
using System.Collections;
using System.Collections.Generic;

namespace Common.Disposables
{
    public sealed class CompositeDisposable : ICollection<IDisposable>, IDisposable
    {
        private readonly object _gate = new();
        private readonly List<IDisposable> _disposables = new();
        
        public void Add(IDisposable disposable) => 
            _disposables.Add(disposable);

        public void CopyTo(IDisposable[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException("arrayIndex");

            lock (_gate)
            {
                var disArray = new List<IDisposable>();
                foreach (var item in _disposables)
                {
                    if (item != null) disArray.Add(item);
                }

                Array.Copy(disArray.ToArray(), 0, array, arrayIndex, array.Length - arrayIndex);
            }
        }

        public int Count => _disposables.Count;
        public bool IsReadOnly => false;

        public bool Remove(IDisposable disposable) => 
            _disposables.Remove(disposable);

        public void Clear() => 
            _disposables.Clear();

        public bool Contains(IDisposable item) => 
            _disposables.Contains(item);

        public void Dispose() => 
            _disposables.ForEach(disposable => disposable?.Dispose());

        public IEnumerator<IDisposable> GetEnumerator() => 
            _disposables.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => 
            GetEnumerator();
    }
}
