using System;
using UnityEngine;

namespace Tools.Common
{
    [Serializable]
    public struct Range<T> where T : IComparable<T>
    {
        [SerializeField]
        private T _start;
        [SerializeField]
        private T _end;

        public T Start => _start;
        public T End => _end;
        
        public Range(T start, T end)
        {
            _start = start;
            _end = end;
        }

        public T Length(Func<T, T, T> subtract)
        {
            return subtract(_end, _start);
        }

        public override string ToString()
        {
            return $"[{_start}, {_end}]";
        }
    }
}
