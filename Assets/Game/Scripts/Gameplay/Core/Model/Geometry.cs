using System;

namespace Gameplay.Core
{
    /// <summary>
    /// Shared Unity-free geometry helpers (RFC §5.2): the single even-odd point-in-polygon test, dedup'd
    /// from the three former copies (PolygonSilhouetteRegion / SampleGridCoverageEvaluator / hit-test).
    /// </summary>
    public static class Geometry
    {
        /// <summary>
        /// Penetration depth (world units) between two convex colliders (TASK-075). Dispatches on primitive so the
        /// detector measures the SAME geometry the physics simulates: circle-circle and circle-polygon are ANALYTIC
        /// (exact for the native <c>CircleCollider2D</c> the physics uses for a <c>Circle</c> piece), while
        /// polygon-polygon delegates to <see cref="ConvexPenetration(Vec2[],Vec2[])"/> — byte-unchanged, so levels
        /// with no circles move by exactly zero. 0 ⇒ apart / merely touching. Deterministic and Unity-free.
        /// </summary>
        public static float Penetration(in ConvexShape a, in ConvexShape b)
        {
            // TASK-178: a concave polyomino is a COMPOUND of convex parts — measure the deepest overlap over every
            // cross pair of parts (the convex SAT kernel is exact per part; the concave contour never reaches it).
            // Non-compound sub-shapes fall straight through to the circle/polygon arms below (the recursion is one
            // level deep — a part is always a plain polygon, a circle is never compound).
            if (a.IsCompound || b.IsCompound)
                return CompoundPenetration(a, b);
            if (a.IsCircle && b.IsCircle)
                return CircleCircle(a.Center, a.Radius, b.Center, b.Radius);
            if (a.IsCircle)
                return CirclePolygon(a.Center, a.Radius, b.Vertices);
            if (b.IsCircle)
                return CirclePolygon(b.Center, b.Radius, a.Vertices);
            return ConvexPenetration(a.Vertices, b.Vertices); // existing SAT kernel — UNCHANGED
        }

        /// <summary>TASK-178: deepest penetration over every cross pair of convex parts (either operand may be a single
        /// non-compound shape, treated as one part). Max, not sum — the honesty metric is the WORST overlap, and two
        /// parts of the SAME body abutting a neighbour must not double-count.</summary>
        private static float CompoundPenetration(in ConvexShape a, in ConvexShape b)
        {
            var pa = a.Parts;
            var pb = b.Parts;
            var max = 0f;
            if (pa != null && pb != null)
            {
                foreach (var partA in pa)
                    foreach (var partB in pb)
                        Widen(ref max, Penetration(Polygon(partA), Polygon(partB)));
            }
            else if (pa != null)
            {
                foreach (var partA in pa)
                    Widen(ref max, Penetration(Polygon(partA), b));
            }
            else
            {
                foreach (var partB in pb)
                    Widen(ref max, Penetration(a, Polygon(partB)));
            }
            return max;

            static void Widen(ref float m, float v) { if (v > m) m = v; }
            static ConvexShape Polygon(Vec2[] v) => ConvexShape.Polygon(v);
        }

        /// <summary>Analytic penetration of two discs: overlap of the radii minus the centre distance, floored at 0.</summary>
        private static float CircleCircle(Vec2 ca, float ra, Vec2 cb, float rb)
        {
            var pen = (ra + rb) - (ca - cb).Length;
            return pen > 0f ? pen : 0f;
        }

        /// <summary>Analytic penetration of a disc against a CONVEX CCW polygon. Finds the closest point on the
        /// polygon boundary (clamped to each edge segment, so corners are handled exactly) and whether the centre
        /// is inside. Depth = the minimum translation to separate: <c>r − dist</c> outside within reach of the
        /// boundary, <c>r + dist</c> when the centre is engulfed, else 0.</summary>
        private static float CirclePolygon(Vec2 c, float r, Vec2[] v)
        {
            ClosestPointOnConvex(c, v, out var dist, out var inside);
            if (inside)
                return r + dist; // centre engulfed: radius + depth to nearest edge
            return dist < r ? r - dist : 0f; // outside: overlap only within r of the boundary
        }

        /// <summary>
        /// SIGNED surface gap (world units) between two convex colliders (TASK-075 reference-rest baseline): &gt;0 is
        /// a clear gap, 0 is exact contact, &lt;0 is overlap (equals <c>−penetration</c>). Circle-circle and
        /// circle-polygon are ANALYTIC (exact for the native disc). Used ONLY by the sim's authored-touching slot
        /// classifier — computed once per level on the static slot poses, never on the live overshoot path (that
        /// stays on <see cref="Penetration"/>). The poly-poly arm is DEFERRED (TASK-075 §12 tech-debt): the
        /// classifier gates on "≥1 circle" before treating a pair as touch-eligible, so this arm is never used to
        /// SELECT a touching pair — it returns <c>−ConvexPenetration</c> when the polygons overlap and
        /// <see cref="float.MaxValue"/> when they are apart (a sentinel "not touching", never a real distance),
        /// which is why a poly-poly pair can never be mis-classified as touching. Deterministic and Unity-free.
        /// </summary>
        public static float Separation(in ConvexShape a, in ConvexShape b)
        {
            // TASK-178: a compound (concave polyomino) is never a circle, so it follows the deferred poly-poly arm —
            // overlap → −penetration, apart → the "not touching" sentinel. It is thus never SELECTED as a touching
            // pair (the classifier gates touching on ≥1 circle), exactly as a plain polygon pair.
            if (a.IsCompound || b.IsCompound)
            {
                var penC = CompoundPenetration(a, b);
                return penC > 0f ? -penC : float.MaxValue;
            }
            if (a.IsCircle && b.IsCircle)
                return (a.Center - b.Center).Length - (a.Radius + b.Radius);
            if (a.IsCircle)
                return CirclePolygonSeparation(a.Center, a.Radius, b.Vertices);
            if (b.IsCircle)
                return CirclePolygonSeparation(b.Center, b.Radius, a.Vertices);
            var pen = ConvexPenetration(a.Vertices, b.Vertices); // poly-poly deferred: overlap → −pen …
            return pen > 0f ? -pen : float.MaxValue;              // … apart → sentinel, never "touching"
        }

        /// <summary>Signed gap of a disc against a CONVEX CCW polygon: <c>dist − r</c> when the centre is outside
        /// (positive gap / negative overlap), <c>−(r + dist)</c> when the centre is engulfed. Mirrors
        /// <see cref="CirclePolygon"/>'s depth exactly (same closest-point), just kept signed for the classifier.</summary>
        private static float CirclePolygonSeparation(Vec2 c, float r, Vec2[] v)
        {
            ClosestPointOnConvex(c, v, out var dist, out var inside);
            return inside ? -(r + dist) : dist - r;
        }

        /// <summary>Distance from a point to a CONVEX CCW polygon's boundary (segment-clamped, so corners are exact)
        /// and whether the point is inside. Shared by <see cref="CirclePolygon"/> (penetration depth) and
        /// <see cref="CirclePolygonSeparation"/> (signed gap) so the two can never disagree on the geometry.</summary>
        private static void ClosestPointOnConvex(Vec2 c, Vec2[] v, out float dist, out bool inside)
        {
            var bestDistSq = float.MaxValue;
            inside = true;
            for (int i = 0, n = v.Length; i < n; i++)
            {
                var a = v[i];
                var e = v[(i + 1) % n] - a;
                var ee = Vec2.Dot(e, e);
                var t = ee < 1e-12f ? 0f : Vec2.Dot(c - a, e) / ee;
                t = t < 0f ? 0f : (t > 1f ? 1f : t); // clamp to segment (correct at corners)
                var p = a + e * t;
                var dsq = (c - p).SqrLength;
                if (dsq < bestDistSq)
                    bestDistSq = dsq;
                // CCW ⇒ outward normal of edge (a→a+e) is (e.Y, −e.X); centre on the outward side of ANY edge ⇒ outside.
                if (Vec2.Dot(c - a, new Vec2(e.Y, -e.X)) > 0f)
                    inside = false;
            }
            dist = MathF.Sqrt(bestDistSq);
        }

        /// <summary>
        /// Separating-Axis penetration depth (world units) between two CONVEX polygons (TASK-042). Returns the
        /// minimum overlap over all edge-normal axes when the polygons intersect, or 0 when a separating axis
        /// exists (they are apart / merely touching). All shape_fill pieces and the floor/cradle statics are
        /// convex, so SAT is exact. Deterministic and Unity-free — the poly-poly kernel of <see cref="Penetration"/>.
        /// Sim-only: the controller's former snap/freeze assist gate that once shared it was removed in TASK-043, so
        /// the ONLY caller today is the sim honesty detector (SimRun.cs).
        /// </summary>
        public static float ConvexPenetration(Vec2[] a, Vec2[] b)
        {
            var min = float.MaxValue;
            if (!MinOverlapOnEdgeNormals(a, b, a, ref min))
                return 0f;
            if (!MinOverlapOnEdgeNormals(a, b, b, ref min))
                return 0f;
            return min == float.MaxValue ? 0f : min;
        }

        /// <summary>Project both polygons on each edge normal of <paramref name="edgesOf"/>; returns false the
        /// moment a separating axis is found (no overlap on that axis), else narrows <paramref name="min"/>.</summary>
        private static bool MinOverlapOnEdgeNormals(Vec2[] a, Vec2[] b, Vec2[] edgesOf, ref float min)
        {
            for (int i = 0, j = edgesOf.Length - 1; i < edgesOf.Length; j = i++)
            {
                var edge = edgesOf[i] - edgesOf[j];
                var axis = new Vec2(-edge.Y, edge.X); // edge normal
                var len = axis.Length;
                if (len < 1e-6f)
                    continue; // degenerate edge
                axis = axis * (1f / len);

                Project(a, axis, out var minA, out var maxA);
                Project(b, axis, out var minB, out var maxB);
                var overlap = MathF.Min(maxA, maxB) - MathF.Max(minA, minB);
                if (overlap <= 0f)
                    return false; // separating axis → no intersection
                if (overlap < min)
                    min = overlap;
            }
            return true;
        }

        private static void Project(Vec2[] verts, Vec2 axis, out float min, out float max)
        {
            min = max = Vec2.Dot(verts[0], axis);
            for (var i = 1; i < verts.Length; i++)
            {
                var d = Vec2.Dot(verts[i], axis);
                if (d < min) min = d;
                else if (d > max) max = d;
            }
        }

        /// <summary>Distance (world units) from a point to a polygon's BOUNDARY (segment-clamped, so corners are
        /// exact). Unlike <see cref="ClosestPointOnConvex"/> it does not compute inside/outside — the tap-halo caller
        /// (TASK-085 tier-2, <see cref="PieceHitResolver"/>) only reaches here when the point is already outside every
        /// polygon, so it needs the rim distance alone. Works for any simple polygon (no CCW/convex requirement).</summary>
        public static float DistanceToPolygonBoundary(Vec2[] verts, Vec2 p)
        {
            var bestDistSq = float.MaxValue;
            for (int i = 0, n = verts.Length; i < n; i++)
            {
                var a = verts[i];
                var e = verts[(i + 1) % n] - a;
                var ee = Vec2.Dot(e, e);
                var t = ee < 1e-12f ? 0f : Vec2.Dot(p - a, e) / ee;
                t = t < 0f ? 0f : (t > 1f ? 1f : t); // clamp to segment (correct at corners)
                var q = a + e * t;
                var dsq = (p - q).SqrLength;
                if (dsq < bestDistSq)
                    bestDistSq = dsq;
            }
            return MathF.Sqrt(bestDistSq);
        }

        /// <summary>Even-odd point-in-polygon over a vertex ring (world/local — caller's frame).</summary>
        public static bool PointInPolygon(Vec2[] verts, Vec2 p)
        {
            var inside = false;
            for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++)
            {
                var vi = verts[i];
                var vj = verts[j];
                if (vi.Y > p.Y != vj.Y > p.Y)
                {
                    var t = (p.Y - vi.Y) / (vj.Y - vi.Y);
                    var x = vi.X + t * (vj.X - vi.X);
                    if (p.X < x)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// TASK-325 (S2): the convex quads of a static SURFACE ribbon — one per polyline segment, offset ±half the
        /// ribbon width along the segment normal. The SINGLE source of truth for "what the ribbon covers", shared by
        /// the view (<c>StaticGeometryView</c> renders these quads) and the validator (<c>ValidateStaticStartOverlap</c>
        /// measures piece embedding against them), so the drawn ribbon and the embed check can never disagree. Each
        /// quad is convex and wound CCW, so a piece resting flush on the surface reads ~0 penetration and a real embed
        /// reads its depth — exactly the semantics of the solid-block SAT, but honest on a CONCAVE curve (each segment
        /// quad is convex; the concave polyline never reaches the convex kernel). Degenerate (&lt;2 points) ⇒ empty.
        /// </summary>
        public static Vec2[][] SurfaceRibbonQuads(Vec2[] polyline, float thickness)
        {
            if (polyline == null || polyline.Length < 2)
                return Array.Empty<Vec2[]>();

            var half = MathF.Max(thickness, 1e-4f) * 0.5f;
            var quads = new Vec2[polyline.Length - 1][];
            for (var i = 0; i < polyline.Length - 1; i++)
            {
                var a = polyline[i];
                var b = polyline[i + 1];
                var d = b - a;
                var len = d.Length;
                // Left-hand normal of a→b: (−dy, dx)/len. Degenerate segment (dup point) falls back to +Y so the quad
                // never collapses to a line (the degeneracy itself is flagged by the validator).
                var n = len > 1e-6f ? new Vec2(-d.Y, d.X) * (1f / len) : new Vec2(0f, 1f);
                var off = n * half;
                quads[i] = new[] { a - off, b - off, b + off, a + off }; // CCW
            }
            return quads;
        }

        /// <summary>A shape's local vertices transformed to world by a pose (rotate then translate).</summary>
        public static Vec2[] WorldVertices(Shape shape, Vec2 position, float rotation)
            => WorldVertices(shape.Vertices, position, rotation);

        /// <summary>A local outline transformed to world by a pose (rotate then translate). Overload for callers
        /// that supply an outline other than <see cref="Shape.Vertices"/> — e.g. the view's higher-resolution
        /// arc tessellation (<c>ShapeVisualOutline</c>, TASK-074).</summary>
        public static Vec2[] WorldVertices(Vec2[] local, Vec2 position, float rotation)
        {
            var world = new Vec2[local.Length];
            for (var i = 0; i < local.Length; i++)
                world[i] = position + local[i].Rotate(rotation);
            return world;
        }
    }
}
