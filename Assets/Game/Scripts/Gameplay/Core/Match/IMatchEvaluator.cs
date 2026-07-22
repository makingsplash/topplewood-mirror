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
    }
}
