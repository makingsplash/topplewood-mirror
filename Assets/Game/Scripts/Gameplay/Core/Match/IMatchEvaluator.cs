using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Win judge (RFC §4): assigns settled pieces to target slots of the same Kind (identity-agnostic,
    /// symmetry-aware angle) and reports how many slots are filled within tolerance. Replaces the removed
    /// coverage-area ICoverageEvaluator.
    /// </summary>
    public interface IMatchEvaluator
    {
        MatchResult Evaluate(TargetFigure target, IReadOnlyList<PlacedPiece> placed, MatchTolerance tol);

        /// <summary>
        /// Same verdict, plus the (slot, piece) assignment the judge built (TASK-634). <paramref name="pairs"/> is
        /// cleared and filled whenever non-null — ALWAYS, before the rigidity check, so it describes the layout and
        /// not the verdict (read it only on a win). Null ⇒ identical to the three-argument overload.
        /// </summary>
        MatchResult Evaluate(TargetFigure target, IReadOnlyList<PlacedPiece> placed, MatchTolerance tol, List<MatchPair> pairs);
    }
}
