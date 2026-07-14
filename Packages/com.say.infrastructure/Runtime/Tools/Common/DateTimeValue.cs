using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Common
{
    [Serializable]
    public class DateTimeValue
    {
        [SerializeField]
        private long _ticks;

        public static  readonly DateTimeValue Empty = new();

        public static implicit operator DateTime(DateTimeValue value) => new(value._ticks, DateTimeKind.Utc);

        public DateTimeValue()
        {
        }

        public DateTimeValue(DateTime dateTime)
        {
            _ticks = dateTime.Ticks;
        }
        
        public DateTime AsLocalTime() => new(_ticks, DateTimeKind.Local);
        public DateTime AsUtcTime() => new(_ticks, DateTimeKind.Utc);
    }
}