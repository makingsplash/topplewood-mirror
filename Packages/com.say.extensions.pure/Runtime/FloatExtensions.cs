using System;

namespace SGS.Extensions
{
    public static class FloatExtensions
    {
        public static bool Aproximate(this float value, float other)
        {
            return Math.Abs(value - other) <= float.Epsilon;
        }
    }
}