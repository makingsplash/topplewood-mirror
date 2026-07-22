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
    /// </summary>
    public sealed class PoseMatchEvaluator : IMatchEvaluator
    {
        private const float Eps = 1e-6f;

        public MatchResult Evaluate(TargetFigure target, IReadOnlyList<PlacedPiece> placed, MatchTolerance tol)
        {
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
            foreach (var c in candidates)
            {
                if (slotUsed[c.Slot] || pieceUsed[c.Piece])
                    continue;
                slotUsed[c.Slot] = true;
                pieceUsed[c.Piece] = true;
                slotsFilled++;
                totalError += c.Error;
            }

            return new MatchResult(slotsFilled == slotsTotal, slotsFilled, slotsTotal, totalError);
        }

        /// <summary>
        /// Reduce an angle delta into the piece's symmetry period and return its magnitude. period = 2π/order
        /// (box order=4 → 90°; triangle/half order=1 → full circle, i.e. reduce into [−π, π]).
        /// </summary>
        private static float SymmetryReduce(float delta, int symmetryOrder)
        {
            var order = symmetryOrder < 1 ? 1 : symmetryOrder;
            var period = (2f * PI) / order;
            var d = delta % period; // (−period, period)
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return Abs(d);
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
