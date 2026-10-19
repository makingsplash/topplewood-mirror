using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// The single, pure two-tier tap resolver (TASK-085) shared by BOTH the domain (<c>ShapeFillMechanicController
    /// .PickIdlePieceAt</c>) and the tutorial gate (<c>TapPieceStep.HitTest</c>) so they can never disagree
    /// on which piece a tap hits (this closes the TASK-078 tech-debt: the tutorial used to hit-test independently).
    ///
    /// tier-1 — CONTAINMENT: the tap lands inside a piece's polygon. Identical to the pre-TASK-085 hit-test
    /// (even-odd point-in-polygon; tie-break by nearest centre = <see cref="HitCandidate.Center"/> distance). When
    /// tier-1 finds a hit, OR the halo radius is 0 (headless/sim), the result is returned as-is — this is what makes
    /// the seeded sweep byte-for-byte identical (r=0 ⇒ tier-2 is never entered and the answer is exactly tier-1).
    ///
    /// tier-2 — HALO FALLBACK (only when nothing contains the tap AND radius &gt; 0): the nearest piece whose polygon
    /// BOUNDARY is within <paramref name="radius"/> world units of the tap. Boundary distance (not centre distance)
    /// gives an even rim around elongated pieces. Overlapping halos are resolved deterministically: minimum boundary
    /// distance first, then the same centre-distance tie-break as tier-1. This only WIDENS which piece a tap hits —
    /// it never adds a delay/queue/confirmation, so "a tap drops the piece immediately" (TASK-069) is preserved.
    /// </summary>
    public static class PieceHitResolver
    {
        /// <summary>One Idle piece the tap may resolve to. <see cref="World"/> is the piece's polygon at its current
        /// pose (world space); <see cref="Center"/> is the pose origin used for the centre-distance tie-break (the
        /// same key the pre-TASK-085 hit-test used).</summary>
        public readonly struct HitCandidate
        {
            public readonly int InstanceId;
            public readonly Vec2[] World;
            public readonly Vec2 Center;

            public HitCandidate(int instanceId, Vec2[] world, Vec2 center)
            {
                InstanceId = instanceId;
                World = world;
                Center = center;
            }
        }

        /// <summary>Resolve the tap to a candidate's <c>InstanceId</c>, or -1 on a miss (InstanceIds are always ≥ 0).</summary>
        public static int Resolve(IReadOnlyList<HitCandidate> candidates, Vec2 p, float radius)
        {
            // tier-1: containment — byte-for-byte the pre-TASK-085 PickIdlePieceAt (first-wins on an equal tie).
            var best = -1;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!Geometry.PointInPolygon(c.World, p))
                    continue;
                var sqr = (c.Center - p).SqrLength;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = c.InstanceId;
                }
            }

            // A tier-1 hit, or a disabled halo (sim/headless), returns exactly tier-1's answer.
            if (best >= 0 || radius <= 0f)
                return best;

            // tier-2: nearest piece whose boundary is within the halo. min boundary distance, then centre-distance.
            var bestDist = float.MaxValue;
            bestSqr = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var d = Geometry.DistanceToPolygonBoundary(c.World, p);
                if (d > radius)
                    continue;
                var sqr = (c.Center - p).SqrLength;
                if (d < bestDist || (d == bestDist && sqr < bestSqr))
                {
                    bestDist = d;
                    bestSqr = sqr;
                    best = c.InstanceId;
                }
            }
            return best;
        }
    }
}
