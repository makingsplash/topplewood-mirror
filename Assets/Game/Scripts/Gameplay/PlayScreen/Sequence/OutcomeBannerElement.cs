using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gameplay.Core;
using Gameplay.Sequence.Base;

namespace Gameplay.PlayScreen.Sequence
{
    /// <summary>
    /// Sequence element that plays the WIN/LOSE banner intro (TASK-116, RFC §3). Async — <c>Finished</c> fires
    /// only after <see cref="IPlayScreenView.ShowOutcomeAsync"/> completes. Data is passed typed through the ctor;
    /// the <c>Start(Dictionary)</c> context is intentionally unused (contract satisfied, arg ignored).
    /// </summary>
    public sealed class OutcomeBannerElement : AsyncLevelSequenceElement
    {
        private readonly IPlayScreenView _view;
        private readonly GameOutcome _outcome;
        private readonly CancellationToken _ct;

        public OutcomeBannerElement(IPlayScreenView view, GameOutcome outcome, CancellationToken ct)
        {
            _view = view;
            _outcome = outcome;
            _ct = ct;
        }

        protected override UniTask StartAsync(Dictionary<string, object> context) =>
            _view.ShowOutcomeAsync(_outcome, _ct);
    }
}
