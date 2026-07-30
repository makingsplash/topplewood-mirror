using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Smoke step (TASK-049 RFC §3): highlight the first idle piece and complete on the first release. It proves
    /// the whole mechanism end-to-end — gate → step → domain-observing completion → MarkCompleted → analytics —
    /// which is the acceptance bar for the SYSTEM scope (one minimal step, owner 2026-07-30).
    ///
    /// Completion signal: the RFC named "<c>AttemptsUsed</c> 0→1", but <see cref="GameModel.AttemptsUsed"/> is a
    /// plain counter with no observable and the domain is untouched (0 edits in Gameplay.Core), so the reactive
    /// proxy for "first piece released" is the first domain-facing input intent. Input is left OPEN, so both the
    /// domain and this step see the tap. (A precise per-piece "release piece X" gate is the AllowOnly follow-up.)
    /// </summary>
    public sealed class ReleaseFirstPieceStep : ITutorialStep
    {
        public string Id => "release_first_piece";

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
