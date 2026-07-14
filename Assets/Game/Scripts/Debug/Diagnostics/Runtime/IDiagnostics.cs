using System;

namespace Diagnostics
{
    public interface IDiagnostics
    {
        IDisposable CreateScope(string name);
    }
}
