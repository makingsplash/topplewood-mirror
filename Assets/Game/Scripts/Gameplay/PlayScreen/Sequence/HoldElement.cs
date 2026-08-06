using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gameplay.Sequence.Base;

namespace Gameplay.PlayScreen.Sequence
{
    /// <summary>
    /// Sequence element that holds statically for a fixed number of seconds (TASK-116, RFC §3) — the owner's
    /// "+ 2 seconds of static" after the banner animation. Async — <c>Finished</c> fires after the delay.
    /// View-agnostic and reusable. Cancelling <c>ct</c> throws <see cref="OperationCanceledException"/> (teardown),
    /// so <c>Finished</c> never fires and the transition is not run.
    /// </summary>
    public sealed class HoldElement : AsyncLevelSequenceElement
    {
        private readonly float _seconds;
        private readonly CancellationToken _ct;

        public HoldElement(float seconds, CancellationToken ct)
        {
            _seconds = seconds;
            _ct = ct;
        }

        protected override UniTask StartAsync(Dictionary<string, object> context) =>
            UniTask.Delay(TimeSpan.FromSeconds(_seconds), cancellationToken: _ct);
    }
}
