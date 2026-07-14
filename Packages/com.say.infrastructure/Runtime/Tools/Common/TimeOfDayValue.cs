using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Common
{
    [Serializable]
    public class TimeOfDayValue
    {
        [SerializeField]
        private long _ticks;

        public static  readonly TimeOfDayValue Empty = new();

        public static implicit operator TimeSpan(TimeOfDayValue value) => new(value._ticks);

        public TimeOfDayValue()
        {
        }

        public TimeOfDayValue(TimeSpan timeSpan)
        {
            _ticks = timeSpan.Ticks;
        }
    }
}