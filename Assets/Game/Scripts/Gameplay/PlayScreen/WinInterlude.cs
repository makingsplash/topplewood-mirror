using System;
using R3;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// A one-shot "number" played between <c>Won</c> and the win screen (TASK-613 plan §3.2). Whoever has something to
    /// show at that moment ARMS it; the resolution flow TAKES it on the next Won and plays it instead of its plain
    /// settle-beat (<see cref="PlayScreenConfig.WinSettleSeconds"/>). The tour's "Совпало" beat is today's only
    /// writer (<c>MatchTargetStep</c>), but neither side knows the other: the flow does not know what a tour is, the
    /// tour does not know the flow — it only arms "the next Won". Context-independent core singleton.
    ///
    /// Last-wins: a newer <see cref="Arm"/> replaces the older number. Disposing an arm handle disarms only if the
    /// number is still THAT handle's — a late dispose of a superseded owner never wipes the current one (the same rule
    /// as <c>TutorialInputGate.AllowOnly</c>). <see cref="TryTake"/> is one-shot: the number is gone once taken.
    /// </summary>
    public sealed class WinInterlude
    {
        private Func<Observable<Unit>> _play;

        /// <summary>Arm <paramref name="play"/> for the next Won. The observable it returns must complete when the
        /// number is over; the flow waits for that (under <see cref="PlayScreenConfig.MaxWinInterludeSeconds"/>).</summary>
        public IDisposable Arm(Func<Observable<Unit>> play)
        {
            _play = play;
            return new ArmHandle(this, play);
        }

        /// <summary>Take the armed number (one-shot). False — nothing armed, the flow keeps its plain pause.</summary>
        public bool TryTake(out Func<Observable<Unit>> play)
        {
            play = _play;
            _play = null;
            return play != null;
        }

        public bool IsArmed => _play != null;

        private void Disarm(Func<Observable<Unit>> play)
        {
            if (_play == play)
                _play = null;
        }

        private sealed class ArmHandle : IDisposable
        {
            private WinInterlude _owner;
            private readonly Func<Observable<Unit>> _play;

            public ArmHandle(WinInterlude owner, Func<Observable<Unit>> play)
            {
                _owner = owner;
                _play = play;
            }

            public void Dispose()
            {
                if (_owner == null)
                    return;
                _owner.Disarm(_play);
                _owner = null; // idempotent
            }
        }
    }
}
