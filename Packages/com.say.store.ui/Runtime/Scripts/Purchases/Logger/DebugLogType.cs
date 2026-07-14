using System;

namespace SGS.Services.DebugLog
{

    public readonly partial struct DebugLogType 
    {
        public static readonly DebugLogType Purchases = new DebugLogType(1 << 6);

    }
}