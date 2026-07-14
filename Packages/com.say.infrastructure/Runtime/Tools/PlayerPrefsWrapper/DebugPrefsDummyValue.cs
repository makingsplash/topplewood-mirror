using System;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public class DebugPrefsDummyValue<T>
    {
#pragma warning disable CS0067
        private readonly T _value;
        public event Action<T> Changed;
        public T Value
        {
            get => _value;
            set {}
        }
#pragma warning restore CS0067
        
        public DebugPrefsDummyValue(string key, T defaultValue, PlayerPrefTags info = default)
        {
            _value = defaultValue;
        }
    }
}