using System;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Reentrant <see cref="ITutorialInputGate"/> (TASK-049 RFC §2, TASK-078 §4). Holds a block depth so
    /// nested/overlapping step blocks compose; input is suppressed while depth &gt; 0. Additionally holds one
    /// optional allow-predicate: while set, only intents matching it pass (used by the step-by-step tutorial to
    /// admit ONLY a tap on the currently highlighted piece). A block still wins over the predicate. Context-
    /// independent — one singleton is shared by both playable scopes and consumed by
    /// <see cref="TutorialGatedInputSource"/>.
    /// </summary>
    public sealed class TutorialInputGate : ITutorialInputGate
    {
        private int _blockDepth;
        private Func<InputIntent, bool> _allow; // null ⇒ no whitelist, everything (not blocked) passes

        public bool IsBlocked => _blockDepth > 0;

        public IDisposable Block()
        {
            _blockDepth++;
            return new BlockHandle(this);
        }

        public IDisposable AllowOnly(Func<InputIntent, bool> predicate)
        {
            _allow = predicate;
            return new AllowHandle(this, predicate);
        }

        // Block has priority: while depth > 0 nothing passes. Otherwise the whitelist decides (or, with no
        // whitelist, everything passes). One method, so the decorator and any caller agree on the gate rule.
        public bool Allows(InputIntent intent) => _blockDepth == 0 && (_allow == null || _allow(intent));

        private readonly Subject<InputIntent> _rejected = new();

        public Observable<InputIntent> Rejected => _rejected;

        // A miss is "the whitelist said no" — not "blocked" (a fall in progress) and not "no whitelist at all".
        public void NotifyRejected(InputIntent intent)
        {
            if (_blockDepth == 0 && _allow != null && !_allow(intent))
                _rejected.OnNext(intent);
        }

        private void Release()
        {
            if (_blockDepth > 0)
                _blockDepth--;
        }

        // Clear the whitelist only if it still points at THIS predicate — a newer AllowOnly (next highlight) has
        // already replaced it, so a late dispose of the old handle must not wipe the current one (last-wins).
        private void ClearAllow(Func<InputIntent, bool> predicate)
        {
            if (_allow == predicate)
                _allow = null;
        }

        private sealed class BlockHandle : IDisposable
        {
            private TutorialInputGate _gate;

            public BlockHandle(TutorialInputGate gate) => _gate = gate;

            public void Dispose()
            {
                if (_gate == null)
                    return;
                _gate.Release();
                _gate = null; // idempotent: a double dispose must not decrement twice
            }
        }

        private sealed class AllowHandle : IDisposable
        {
            private TutorialInputGate _gate;
            private readonly Func<InputIntent, bool> _predicate;

            public AllowHandle(TutorialInputGate gate, Func<InputIntent, bool> predicate)
            {
                _gate = gate;
                _predicate = predicate;
            }

            public void Dispose()
            {
                if (_gate == null)
                    return;
                _gate.ClearAllow(_predicate);
                _gate = null; // idempotent
            }
        }
    }
}
