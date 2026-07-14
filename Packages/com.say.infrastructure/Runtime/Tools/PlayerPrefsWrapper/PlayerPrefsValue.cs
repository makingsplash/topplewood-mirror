using System;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public abstract class PlayerPrefsValue<T> : PlayerPrefsBase
    {
        public event Action<T> OnChanged;

        protected T RealtimeValue;

        protected PlayerPrefsValue(string key, PlayerPrefTags info) : base(key, info)
        {
        }

        public T Value
        {
            get => RealtimeValue;
            set
            {
                if (!ShouldSave(value))
                {
                    return;
                }

                RealtimeValue = value;
                SaveToStorage(value);
                OnChanged?.Invoke(RealtimeValue);
            }
        }
        
        public void Track(Action<T> onNext)
        {
            OnChanged += onNext;
            onNext(Value);
        }
        public void Untrack(Action<T> onNext)
        {
            OnChanged -= onNext;
        }
        
        protected abstract void SaveToStorage(T value);

        protected virtual bool ShouldSave(T newValue)
        {
            return !newValue.Equals(RealtimeValue);
        }

        protected void ChangedInvoke()
        {
            OnChanged?.Invoke(RealtimeValue);
        }

        public override void ForceSave()
        {
            SaveToStorage(RealtimeValue);
        }
    }
}