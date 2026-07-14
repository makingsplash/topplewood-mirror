using System.Collections.Generic;

namespace SGS.Store.Purchases.ConditionParser
{
    internal class PeekableEnumerator<T> : IEnumerator<T>
    {
        private IEnumerator<T> _peekingEnumerator;
        private T _current;
        private T _peeked;

        public PeekableEnumerator(IEnumerable<T> enumerable)
        {
            _peekingEnumerator = enumerable.GetEnumerator();
            MoveNextInternal();
        }

        public T Current => _current;
        public T Peek => _peeked;
        public bool CanPeek => _peeked != null;

        object System.Collections.IEnumerator.Current => _current;

        public bool MoveNext()
        {
            _current = _peeked;

            // if reached end of the enumeration
            if (Equals(_current, default(T)))
            {
                return false;
            }

            MoveNextInternal();
            return true;
        }

        private void MoveNextInternal()
        {
            if (_peekingEnumerator.MoveNext())
            {
                _peeked = _peekingEnumerator.Current;
            }
            else
            {
                _peeked = default(T);
            }
        }

        public void Reset()
        {
            _peekingEnumerator.Reset();

            MoveNextInternal();
        }

        public void Dispose()
        {
            _peekingEnumerator.Dispose();
        }
    }
}