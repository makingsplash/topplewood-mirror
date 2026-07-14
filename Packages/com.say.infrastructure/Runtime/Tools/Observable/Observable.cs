using System;

namespace SGS.Infrastructure.Tools.Observable
{
    [Serializable]
    public class Observable<T> where T : IEquatable<T>
    {
        public event Action<T> OnChanged;
        
        [Obsolete("Use OnChanged instead")]
        public event Action<T> Changed { 
            add => OnChanged += value;
            remove => OnChanged -= value;
        }
            
        private T _value;
        
        public T Value
        {
            get => _value;
            set
            {
                if (value.Equals(_value))
                {
                    return;
                }
                
                _value = value;
                OnChanged?.Invoke(value);
            }
        }
 
        public Observable()
        {
        }
 
        public Observable(T value)
        {
            _value = value;
        }
 
        public override string ToString()
        {
            return _value.ToString();
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
    }
    
    [Serializable]
    public class ObservableEnum<T> where T : Enum
    {
        public event Action<T> OnChanged;
        
        [Obsolete("Use OnChanged instead")]
        public event Action<T> Changed { 
            add => OnChanged += value;
            remove => OnChanged -= value;
        }
        
        private T _value;
        
        public T Value
        {
            get => _value;
            set
            {
                if (value.Equals(_value))
                {
                    return;
                }
                
                _value = value;
                OnChanged?.Invoke(value);
            }
        }
 
        public ObservableEnum()
        {
        }
 
        public ObservableEnum(T value)
        {
            _value = value;
        }
 
        public override string ToString()
        {
            return _value.ToString();
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
    }
}