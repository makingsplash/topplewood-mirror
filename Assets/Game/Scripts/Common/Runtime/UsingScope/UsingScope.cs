using System;

namespace Common.UsingScope
{
    public sealed class UsingScope : IDisposable
    {
        private readonly Action _onDispose;

        public UsingScope(Action onStart, Action onDispose)
        {
            _onDispose = onDispose;
            onStart?.Invoke();
        }

        public void Dispose() =>
            _onDispose?.Invoke();
    }
    
    public sealed class UsingScope<TState> : IDisposable
    {
        private readonly TState _state;
        private readonly Action<TState> _onDispose;
        private bool _isDisposed;

        public UsingScope(TState state, Action<TState> onStart, Action<TState> onDispose)
        {
            _state = state;
            _onDispose = onDispose;
            onStart?.Invoke(state);
        }

        public void Dispose()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);
            
            _onDispose?.Invoke(_state);
            _isDisposed = true;
        }
    }
}
