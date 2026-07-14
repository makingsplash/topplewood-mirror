using System;

using SGS.Infrastructure.Tools.PlayerPrefsWrapper;

namespace SGS.Infrastructure.Core
{
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }
    
    public sealed class SystemTimeProvider : ITimeProvider
    {
        public static readonly SystemTimeProvider Instance = new SystemTimeProvider();

        public DateTime Now => DateTime.Now;

        private SystemTimeProvider()
        {
        }
    }

    public class DebugTimeProvider : ITimeProvider
    {
        private readonly PlayerPrefsTimeSpan _storage;

        private TimeSpan Offset => _storage.Value;

        public DateTime Now => DateTime.Now + Offset;

        public DebugTimeProvider(PlayerPrefsTimeSpan storage)
        {
            _storage = storage;
        }
    }
}