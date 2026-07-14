using System;

namespace Tools.Constants
{
    public static class Constants
    {
        public static float NavMeshPathEpsilon = 0.01f;
        public static TimeSpan TimeSpanEpsilon => TimeSpan.FromMilliseconds(1);
    }
}
