
using System;

namespace Tools.Purchases.Data.Common
{
#if !SAY_INFRASTURCTURE
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }
#endif
}
