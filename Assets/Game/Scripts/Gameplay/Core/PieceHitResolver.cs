using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// The single, pure two-tier tap resolver (TASK-085) shared by BOTH the domain (<c>ShapeFillMechanicController
    /// .PickIdlePieceAt</c>) and the tutorial gate (<c>TapPieceStep.HitTest</c>) so they can never disagree
    /// on which piece a tap hits (this closes the TASK-078 tech-debt: the tutorial used to hit-test independently).
    ///
    /// tier-1 — CONTAINMENT: the tap lands inside a piece's polygon (even-odd point-in-polygon). Among the pieces that
    /// contain it, the one DRAWN ON TOP wins (TASK-645): the lowest <see cref="PieceStacking.Rank"/>, the same rule
    /// <c>PieceDepth</c> draws by. So a tap on the uncovered part of a lower piece takes the lower piece, a tap in the
    /// overlap takes the upper one, and a fully covered piece cannot be tapped until its cover is gone — the tap is what
    /// the player SEES. Equal rank (only ≥ <see cref="PieceStacking.Slots"/>, where the draw order is undefined too) →
    /// the pre-TASK-645 nearest-centre tie-break (<see cref="HitCandidate.Center"/> distance), then first-wins. When
    /// tier-1 finds a hit, OR the halo radius is 0 (headless/sim), the result is returned as-is (r=0 ⇒ tier-2 is never
    /// entered and the answer is exactly tier-1).
    ///
    /// tier-2 — HALO FALLBACK (only when nothing contains the tap AND radius &gt; 0): the nearest piece whose polygon
    /// BOUNDARY is within <paramref name="radius"/> world units of the tap. Boundary distance (not centre distance)
    /// gives an even rim around elongated pieces. Overlapping halos are resolved deterministically: minimum boundary
    /// distance first, then the upper piece (rank — a shared edge of a cover and the piece under it goes to the cover),
    /// then the centre-distance tie-break. This only WIDENS which piece a tap hits — it never adds a
    /// delay/queue/confirmation, so "a tap drops the piece immediately" (TASK-069) is preserved.
    /// </summary>
    public static class PieceHitResolver
    {
        /// <summary>Grid resolution of <see cref="FindAim"/> per bbox axis.</summary>
        public const int AimGrid = 24;

        /// <summary>One Idle piece the tap may resolve to. <see cref="World"/> is the piece's polygon at its current
        /// pose (world space); <see cref="Center"/> is the pose origin used for the centre-distance tie-break (the
        /// same key the pre-TASK-085 hit-test used); <see cref="Rank"/> is its draw rank (lower = on top).</summary>
        public readonly struct HitCandidate
        {
            public readonly int InstanceId;
            public readonly Vec2[] World;
            public readonly Vec2 Center;
            public readonly int Rank;

            /// <param name="placementIndex">The piece's index in the level — the rank comes from
            /// <see cref="PieceStacking.Rank"/>, never computed by the caller.</param>
            public HitCandidate(int instanceId, int placementIndex, Vec2[] world, Vec2 center)
            {
                InstanceId = instanceId;
                World = world;
                Center = center;
                Rank = PieceStacking.Rank(placementIndex);
            }
        }

        /// <summary>How much of a piece the player can tap (<see cref="FindAim"/>).</summary>
        public enum Visibility
        {
            /// <summary>Nothing above it overlaps it — every point of it taps it.</summary>
            Full,

            /// <summary>Something above covers part of it; the rest still taps it.</summary>
            Partial,

            /// <summary>Covered entirely (to the grid's resolution): no point taps it until the cover is gone.</summary>
            Hidden,
        }

        /// <summary>Resolve the tap to a candidate's <c>InstanceId</c>, or -1 on a miss (InstanceIds are always ≥ 0).</summary>
        public static int Resolve(IReadOnlyList<HitCandidate> candidates, Vec2 p, float radius)
        {
            // tier-1: containment — the piece drawn on top wins; nearest centre only among equal ranks.
            var best = -1;
            var bestRank = int.MaxValue;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!Geometry.PointInPolygon(c.World, p))
                    continue;
                var sqr = (c.Center - p).SqrLength;
                if (c.Rank < bestRank || (c.Rank == bestRank && sqr < bestSqr))
                {
                    bestRank = c.Rank;
                    bestSqr = sqr;
                    best = c.InstanceId;
                }
            }

            // A tier-1 hit, or a disabled halo (sim/headless), returns exactly tier-1's answer.
            if (best >= 0 || radius <= 0f)
                return best;

            // tier-2: nearest piece whose boundary is within the halo. min boundary distance, then rank, then centre.
            var bestDist = float.MaxValue;
            bestSqr = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var d = Geometry.DistanceToPolygonBoundary(c.World, p);
                if (d > radius)
                    continue;
                var sqr = (c.Center - p).SqrLength;
                if (d < bestDist
                    || (d == bestDist && (c.Rank < bestRank || (c.Rank == bestRank && sqr < bestSqr))))
                {
                    bestDist = d;
                    bestRank = c.Rank;
                    bestSqr = sqr;
                    best = c.InstanceId;
                }
            }
            return best;
        }

        /// <summary>
        /// TASK-645: where a finger (the tour's, the sim bot's) must touch to tap <paramref name="targetId"/>, judged by
        /// <see cref="Resolve"/> itself (r=0), so "the aim taps the target" holds by construction. Samples an
        /// <see cref="AimGrid"/>² grid over the target's bbox and keeps, among the cells that resolve to the target, the
        /// one deepest inside its visible region — farthest from the target's own boundary and from every boundary of a
        /// piece that would win over it (a coarse pole of inaccessibility, cf. Mapbox polylabel, on the region
        /// "target minus covers" without polygon booleans). <see cref="Visibility.Full"/> when no cell is lost to a
        /// cover; the aim is then the deepest point of the whole piece. <paramref name="aim"/> is meaningful unless
        /// <see cref="Visibility.Hidden"/> (unknown target → Hidden).
        /// </summary>
        public static Visibility FindAim(IReadOnlyList<HitCandidate> candidates, int targetId, out Vec2 aim)
        {
            aim = default;
            var t = -1;
            for (var i = 0; i < candidates.Count; i++)
                if (candidates[i].InstanceId == targetId)
                    t = i;
            if (t < 0)
                return Visibility.Hidden;

            var target = candidates[t];
            var min = target.World[0];
            var max = target.World[0];
            foreach (var v in target.World)
            {
                min = new Vec2(System.MathF.Min(min.X, v.X), System.MathF.Min(min.Y, v.Y));
                max = new Vec2(System.MathF.Max(max.X, v.X), System.MathF.Max(max.Y, v.Y));
            }

            var covered = false;
            var found = false;
            var bestClear = float.MinValue;
            for (var iy = 0; iy < AimGrid; iy++)
            for (var ix = 0; ix < AimGrid; ix++)
            {
                // Cell centres, never the bbox edge: a point on a boundary is where even-odd is unreliable.
                var p = new Vec2(min.X + (max.X - min.X) * (ix + 0.5f) / AimGrid,
                    min.Y + (max.Y - min.Y) * (iy + 0.5f) / AimGrid);
                if (!Geometry.PointInPolygon(target.World, p))
                    continue;
                if (Resolve(candidates, p, 0f) != targetId)
                {
                    covered = true;
                    continue;
                }

                var clear = Geometry.DistanceToPolygonBoundary(target.World, p);
                for (var i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (i != t && c.Rank <= target.Rank)
                        clear = System.MathF.Min(clear, Geometry.DistanceToPolygonBoundary(c.World, p));
                }
                if (clear > bestClear)
                {
                    bestClear = clear;
                    aim = p;
                    found = true;
                }
            }

            if (!found)
                return Visibility.Hidden;
            return covered ? Visibility.Partial : Visibility.Full;
        }
    }
}
