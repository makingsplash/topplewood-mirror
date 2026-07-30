using System;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Reentrant <see cref="ITutorialInputGate"/> (TASK-049 RFC §2). Holds a block depth so nested/overlapping
    /// step blocks compose; input is suppressed while depth &gt; 0. Context-independent — one singleton is shared
    /// by both playable scopes and consumed by <see cref="TutorialGatedInputSource"/>.
    /// </summary>
    public sealed class TutorialInputGate : ITutorialInputGate
    {
        private int _blockDepth;

        public bool IsBlocked => _blockDepth > 0;

        public IDisposable Block()
        {
            _blockDepth++;
            return new Handle(this);
        }

        private void Release()
        {
            if (_blockDepth > 0)
                _blockDepth--;
        }

        private sealed class Handle : IDisposable
        {
            private TutorialInputGate _gate;

            public Handle(TutorialInputGate gate) => _gate = gate;

            public void Dispose()
            {
                if (_gate == null)
                    return;
                _gate.Release();
                _gate = null; // idempotent: a double dispose must not decrement twice
            }
        }
    }
}
