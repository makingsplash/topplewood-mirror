#if !SAY_INFRASTURCTURE

using System;
using UnityEngine;

namespace Tools.Purchases.Data.Common
{
    //TimeSpan values cannot be serialized in Unity, so to edit value we need this custom class (and custom editor)
    [Serializable]
    public class TimeSpanValue
    {
        [SerializeField]
        private long _ticks;

        public static  readonly TimeSpanValue Empty = new();

        public static implicit operator TimeSpan(TimeSpanValue value) => new(value._ticks);

        public TimeSpanValue()
        {
        }

        public TimeSpanValue(TimeSpan timeSpan)
        {
            _ticks = timeSpan.Ticks;
        }
    }
    
    //DateTime values cannot be serialized in Unity, so to edit value we need this custom class (and custom editor)
}
#endif