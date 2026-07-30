using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Smoke step (TASK-049 RFC §3): highlight the first idle piece and complete on the first tap. It proves the
    /// whole mechanism end-to-end — gate → step → domain-observing completion → MarkCompleted → analytics — which
    /// is the acceptance bar for the SYSTEM scope (one minimal step, owner 2026-07-30).
    ///
    /// Completion signal — and why the name is exactly "first tap", not "release first piece": the RFC named
    /// "<c>AttemptsUsed</c> 0→1", but <see cref="GameModel.AttemptsUsed"/> is a plain counter with no observable
    /// and the domain is untouched (0 edits in Gameplay.Core), so this step completes on the first domain-facing
    /// input intent — which fires on ANY tap, including a miss on empty space (the domain hit-tests afterwards and
    /// releases nothing). This id is what reaches the analytics funnel, so it must claim only what it measures: a
    /// first tap, NOT a confirmed piece release. A precise per-piece "release piece X" gate — completing only when
    /// the highlighted piece actually leaves Idle — is the AllowOnly(pred) follow-up (RFC §8); honestly observing
    /// that without a domain observable or an Update poll is not cheap (QA §3.A), so v1 stays this honest proxy.
    /// Input is left OPEN, so both the domain and this step see the tap.
    /// </summary>
    public sealed class FirstTapStep : ITutorialStep
    {
        public string Id => "first_tap";

        public Observable<Unit> Enter(TutorialContext context)
        {
            HighlightFirstIdlePiece(context);
            return context.Inputs.Take(1).Select(static _ => Unit.Default);
        }

        public void Exit(TutorialContext context) => context.View.Clear();

        private static void HighlightFirstIdlePiece(TutorialContext context)
        {
            var pieces = context.Model.Pieces;
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece.State != PieceLifecycle.Idle)
                    continue;
                context.View.Highlight(piece.Position);
                return;
            }
        }
    }
}
