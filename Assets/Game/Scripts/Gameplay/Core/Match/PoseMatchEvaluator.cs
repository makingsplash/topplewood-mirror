using System.Collections.Generic;
using static System.MathF;

namespace Gameplay.Core
{
    /// <summary>
    /// Per-piece pose match (RFC §4.2), Unity-free and deterministic. Groups slots and placed pieces by
    /// Kind; within a Kind, greedily assigns the lowest-cost admissible (slot, piece) pair — admissible when
    /// <c>posErr ≤ posTol</c> AND symmetry-reduced <c>angErr ≤ angTol</c>. A slot is filled when it gets an
    /// admissible piece. Greedy is sufficient for MVP layouts (few pieces, pieces above their slots, generous
    /// tolerance); optimal assignment (Hungarian) is deferred (TECH_DEBT) if QA/level-sim finds false negatives.
    ///
    /// TASK-043 adds a rigidity constraint (RFC §2). After the assist layer was removed the win is judged from
    /// physical rest poses, so the absolute per-piece tolerances above can no longer distinguish a genuinely
    /// assembled figure from one that collapsed with every piece still inside its own generous tolerance. When
    /// <c>tol.RigidityTol &gt; 0</c> the match additionally requires the spread of the matched pieces' residual
    /// vectors (<c>piece_i − slot_i</c>, including the pivot) to stay within that bound; a large spread means the
    /// figure is internally deformed and the silhouette is NOT considered assembled.
    /// </summary>
    public sealed class PoseMatchEvaluator : IMatchEvaluator
    {
        private const float Eps = 1e-6f;

        public MatchResult Evaluate(TargetFigure target, IReadOnlyList<PlacedPiece> placed, MatchTolerance tol)
            => Evaluate(target, placed, tol, null);

        /// <inheritdoc />
        public MatchResult Evaluate(TargetFigure target, IReadOnlyList<PlacedPiece> placed, MatchTolerance tol, List<MatchPair> pairs)
        {
            pairs?.Clear();
            var slots = target?.Slots;
            var slotsTotal = slots?.Count ?? 0;
            if (slotsTotal == 0)
                return new MatchResult(placed == null || placed.Count == 0, 0, 0, 0f);

            var posTol = tol.PosTol <= 0f ? Eps : tol.PosTol;
            var angTol = tol.AngTol;

            var slotUsed = new bool[slotsTotal];
            var pieceUsed = placed == null ? new bool[0] : new bool[placed.Count];

            // Enumerate all admissible (slot, piece) candidate pairs with a normalised cost, then assign
            // greedily by ascending cost. n is tiny (≤ a handful) so O(n²) enumeration is fine.
            var candidates = new List<Candidate>();
            for (var s = 0; s < slotsTotal; s++)
            {
                var slot = slots[s];
                if (placed == null)
                    continue;
                for (var p = 0; p < placed.Count; p++)
                {
                    var piece = placed[p];
                    if (piece.Kind != slot.Kind)
                        continue;

                    var posErr = (slot.Position - piece.Position).Length;
                    if (posErr > posTol)
                        continue;

                    var angErr = SymmetryReduce(piece.Rotation - slot.Rotation, piece.SymmetryOrder);
                    if (angErr > angTol)
                        continue;

                    var cost = posErr / posTol + angErr / (angTol <= 0f ? Eps : angTol);
                    candidates.Add(new Candidate(s, p, cost, posErr + angErr));
                }
            }

            candidates.Sort(static (a, b) => a.Cost.CompareTo(b.Cost));

            var slotsFilled = 0;
            var totalError = 0f;
            // Residual vector (piece − slot) of each matched pair — the rigidity spread is measured over these.
            var residuals = new List<Vec2>(slotsTotal);
            foreach (var c in candidates)
            {
                if (slotUsed[c.Slot] || pieceUsed[c.Piece])
                    continue;
                slotUsed[c.Slot] = true;
                pieceUsed[c.Piece] = true;
                slotsFilled++;
                totalError += c.Error;
                residuals.Add(placed[c.Piece].Position - slots[c.Slot].Position);
                // TASK-634: the layout, recorded before the rigidity check (the caller reads it only on a win).
                pairs?.Add(new MatchPair(c.Slot, c.Piece));
            }

            var assembled = slotsFilled == slotsTotal;

            // Rigidity constraint (TASK-043 §2): the figure is only assembled when the pairwise spread of the
            // residual vectors stays within RigidityTol. Translation-invariant (a uniform shift has zero spread —
            // that is PosTol's job), so it measures INTERNAL deformation: a canonical rest has all residuals ≈
            // equal (spread ≈ solver slop), a collapsed figure has residuals pulling in opposite directions
            // (large spread). n ≤ 1 residual ⇒ spread 0 ⇒ never blocks. Off when RigidityTol ≤ 0 (default).
            if (assembled && tol.RigidityTol > 0f && RigidityViolation(residuals) > tol.RigidityTol)
                assembled = false;

            return new MatchResult(assembled, slotsFilled, slotsTotal, totalError);
        }

        /// <summary>Max pairwise distance between residual vectors — the internal-deformation spread (TASK-043 §2).
        /// O(n²) over the handful of matched pieces. Zero for 0 or 1 residual.</summary>
        private static float RigidityViolation(List<Vec2> residuals)
        {
            var worst = 0f;
            for (var i = 0; i < residuals.Count; i++)
                for (var j = i + 1; j < residuals.Count; j++)
                {
                    var spread = (residuals[i] - residuals[j]).Length;
                    if (spread > worst)
                        worst = spread;
                }
            return worst;
        }

        /// <summary>
        /// Reduce an angle delta into the piece's symmetry period and return its magnitude. period = 2π/order
        /// (box order=4 → 90°; triangle/half order=1 → full circle, i.e. reduce into [−π, π]).
        /// </summary>
        private static float SymmetryReduce(float delta, int symmetryOrder) => Abs(SignedSymmetryDelta(delta, symmetryOrder));

        /// <summary>
        /// The signed form of <see cref="SymmetryReduce"/>: <paramref name="delta"/> folded into
        /// [−period/2, period/2] with period = 2π/order. TASK-634 lands a won piece on <c>rest − SignedSymmetryDelta(rest
        /// − slot)</c> — the slot angle NEAREST its rest pose — so a box that settled at 90° does not spin a quarter
        /// turn in flight. Its magnitude is the judge's angle error bit for bit (one code path).
        /// </summary>
        public static float SignedSymmetryDelta(float delta, int symmetryOrder)
        {
            var order = symmetryOrder < 1 ? 1 : symmetryOrder;
            var period = (2f * PI) / order;
            var d = delta % period; // (−period, period)
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return d;
        }

        private readonly struct Candidate
        {
            public readonly int Slot;
            public readonly int Piece;
            public readonly float Cost;
            public readonly float Error;

            public Candidate(int slot, int piece, float cost, float error)
            {
                Slot = slot;
                Piece = piece;
                Cost = cost;
                Error = error;
            }
        }
    }
}
