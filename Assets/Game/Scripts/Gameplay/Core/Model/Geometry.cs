using System;
using System.Collections.Generic;

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

        /// <summary>
        /// TASK-087: Euclidean GAP (world units) between two SIMPLE polygons — concave allowed, any winding. 0 when they
        /// touch, cross, or one lies inside the other. The tap-spacing audit's edge-to-edge metric: two pieces' tap
        /// targets (polygon ⊕ halo disc, <see cref="PieceHitResolver"/>) stop overlapping exactly when this reaches the
        /// halo diameter. Standard brute force (MATLAB/matgeom "distancePolygons", PostGIS dist2d): intersection test
        /// first, then the minimum over vertex→edge pairs in BOTH directions, because for two disjoint polygons the
        /// closest pair always involves a vertex of one and an edge of the other. O(n·m) — piece outlines are tens of
        /// vertices, so the per-level audit stays in milliseconds. Not <see cref="Separation"/>: its poly-poly arm returns
        /// a "not touching" sentinel, and SAT (<see cref="ConvexPenetration"/>) is convex-only.
        /// </summary>
        public static float PolygonGap(Vec2[] a, Vec2[] b)
        {
            // Containment without crossing edges (one polygon swallowed by the other): any vertex inside ⇒ overlap.
            foreach (var v in a)
                if (PointInPolygon(b, v))
                    return 0f;
            foreach (var v in b)
                if (PointInPolygon(a, v))
                    return 0f;

            // A cross with no vertex inside the other (two planks laid over each other) is caught only by the edges.
            for (int i = 0, n = a.Length; i < n; i++)
                for (int j = 0, m = b.Length; j < m; j++)
                    if (SegmentsIntersect(a[i], a[(i + 1) % n], b[j], b[(j + 1) % m]))
                        return 0f;

            var gap = float.MaxValue;
            foreach (var v in a)
                gap = MathF.Min(gap, DistanceToPolygonBoundary(b, v));
            foreach (var v in b)
                gap = MathF.Min(gap, DistanceToPolygonBoundary(a, v));
            return gap;
        }

        /// <summary>Closed-segment intersection p1p2 × q1q2: proper crossing, or an endpoint lying on the other segment
        /// (touching / collinear overlap) — both mean the polygons share a point, so their gap is 0.</summary>
        private static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            var d1 = TriArea2(q1, q2, p1);
            var d2 = TriArea2(q1, q2, p2);
            var d3 = TriArea2(p1, p2, q1);
            var d4 = TriArea2(p1, p2, q2);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
                return true;
            return (d1 == 0f && OnSegment(q1, q2, p1)) || (d2 == 0f && OnSegment(q1, q2, p2))
                || (d3 == 0f && OnSegment(p1, p2, q1)) || (d4 == 0f && OnSegment(p1, p2, q2));

            // Collinear point p within the bounding box of segment a-b ⇒ on the segment.
            static bool OnSegment(Vec2 a, Vec2 b, Vec2 p)
                => MathF.Min(a.X, b.X) <= p.X && p.X <= MathF.Max(a.X, b.X)
                && MathF.Min(a.Y, b.Y) <= p.Y && p.Y <= MathF.Max(a.Y, b.Y);
        }

        /// <summary>
        /// TASK-349: distance (world units) from a point to a polygon's BOUNDARY AND the index of the boundary edge that
        /// carries that distance (edge <c>i</c> is <c>verts[i]→verts[i+1]</c>). ONE closest-point pass, so the distance
        /// and the edge can never disagree — the SDF concave fill (<c>PieceMeshBuilder</c>) needs BOTH a per-vertex
        /// distance-to-silhouette and the outward normal of the SAME nearest edge, and computing them separately let the
        /// two drift on a corner. Segment-clamped (corners exact), any simple polygon (no CCW/convex requirement); the
        /// returned distance equals <see cref="DistanceToPolygonBoundary"/> for the same input.
        /// </summary>
        public static float NearestBoundaryEdge(Vec2[] verts, Vec2 p, out int edgeIndex)
        {
            var bestDistSq = float.MaxValue;
            edgeIndex = 0;
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
                {
                    bestDistSq = dsq;
                    edgeIndex = i;
                }
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

        /// <summary>
        /// TASK-349: a static SURFACE ribbon as ONE continuous joined strip (the ImGui/NVIDIA polyline-stroking single-
        /// strip: consecutive segments SHARE their join vertices so the ribbon is unbroken — no per-segment quad cut
        /// edges). VIEW-ONLY: the validator's embed check and the poseHash still measure <see cref="SurfaceRibbonQuads"/>
        /// (frozen); this only changes how the ribbon is DRAWN, so the notch defect (each segment quad drew its own
        /// perimeter ⇒ a chain of outlined links) is removed without moving a single surface vertex.
        /// <para>
        /// Three vertex rows per polyline point — LEFT rail (cross <c>+half</c>), CENTRE (<c>0</c>), RIGHT rail
        /// (<c>−half</c>) — so the wood shader's edge band, fed <c>edge = half − |cross|</c>, appears ONLY on the two long
        /// rails (<c>|cross| → half ⇒ edge → 0</c>) and never on the mid-ribbon join cuts (<c>edge ≈ half</c>, large).
        /// Rails are offset along the MITER normal (average of the two incident segment normals), clamped to a miter
        /// limit so a sharp corner falls back to a flat clip instead of a spike; a smooth chute is all miters. This is
        /// the SDF idea specialised to a strip: distance to the ribbon's two rails.
        /// </para>
        /// </summary>
        public static RibbonStrip SurfaceRibbonStrip(Vec2[] polyline, float thickness)
        {
            if (polyline == null || polyline.Length < 2)
                return RibbonStrip.Empty;

            const float miterLimit = 4f; // sharper than this ⇒ clamp the miter to a flat clip (no spike)
            var half = MathF.Max(thickness, 1e-4f) * 0.5f;
            var m = polyline.Length;

            // Per-segment left-hand unit normal (−dy, dx)/len — the same normal SurfaceRibbonQuads offsets by.
            var segN = new Vec2[m - 1];
            for (var i = 0; i < m - 1; i++)
            {
                var d = polyline[i + 1] - polyline[i];
                var len = d.Length;
                segN[i] = len > 1e-6f ? new Vec2(-d.Y, d.X) * (1f / len) : new Vec2(0f, 1f);
            }

            var verts = new Vec2[3 * m];
            var cross = new float[3 * m];
            var normal = new Vec2[3 * m];
            for (var i = 0; i < m; i++)
            {
                // Miter normal at this point: at the ends it is the single incident segment normal; inside it is the
                // averaged normal, and the miter LENGTH stretches by 1/cos(halfAngle) so the rail stays a constant
                // half-width from the centreline — clamped so a near-reversed corner cannot shoot a spike.
                Vec2 n;
                float miterLen;
                if (i == 0) { n = segN[0]; miterLen = half; }
                else if (i == m - 1) { n = segN[m - 2]; miterLen = half; }
                else
                {
                    var sum = segN[i - 1] + segN[i];
                    var slen = sum.Length;
                    n = slen > 1e-6f ? sum * (1f / slen) : segN[i];
                    var denom = Vec2.Dot(n, segN[i]);              // cos(halfAngle) ∈ (0,1]
                    denom = denom < 1f / miterLimit ? 1f / miterLimit : denom;
                    miterLen = half / denom;
                }

                var p = polyline[i];
                var off = n * miterLen;
                var b = 3 * i;
                verts[b + 0] = p + off; cross[b + 0] = +half; normal[b + 0] = n;   // left rail
                verts[b + 1] = p;       cross[b + 1] = 0f;    normal[b + 1] = n;    // centreline
                verts[b + 2] = p - off; cross[b + 2] = -half; normal[b + 2] = new Vec2(-n.X, -n.Y); // right rail
            }

            // Two quad columns (left rail↔centre, centre↔right rail) welded across every join → one connected strip.
            var tris = new int[(m - 1) * 12];
            var t = 0;
            for (var i = 0; i < m - 1; i++)
            {
                var a = 3 * i;
                var c = 3 * (i + 1);
                // left column: aL,aC,cC,cL
                Quad(tris, ref t, a + 0, a + 1, c + 1, c + 0);
                // right column: aC,aR,cR,cC
                Quad(tris, ref t, a + 1, a + 2, c + 2, c + 1);
            }

            return new RibbonStrip(verts, tris, cross, normal, half);

            static void Quad(int[] buf, ref int t, int v0, int v1, int v2, int v3)
            {
                buf[t++] = v0; buf[t++] = v1; buf[t++] = v2;
                buf[t++] = v0; buf[t++] = v2; buf[t++] = v3;
            }
        }

        /// <summary>TASK-349: one continuous ribbon strip — vertices, triangles, a per-vertex signed CROSS offset from the
        /// centreline (<c>±half</c> on the rails, <c>0</c> on the centre) and the per-vertex outward rail NORMAL. The view
        /// turns <c>Cross</c> into the shader's edge distance (<c>Half − |Cross|</c>); the validator never sees it.</summary>
        public readonly struct RibbonStrip
        {
            public readonly Vec2[] Vertices;
            public readonly int[] Triangles;
            public readonly float[] Cross;
            public readonly Vec2[] Normal;
            public readonly float Half;

            public RibbonStrip(Vec2[] vertices, int[] triangles, float[] cross, Vec2[] normal, float half)
            {
                Vertices = vertices;
                Triangles = triangles;
                Cross = cross;
                Normal = normal;
                Half = half;
            }

            public bool IsEmpty => Vertices == null || Vertices.Length == 0;

            public static RibbonStrip Empty =>
                new RibbonStrip(Array.Empty<Vec2>(), Array.Empty<int>(), Array.Empty<float>(), Array.Empty<Vec2>(), 0f);
        }

        /// <summary>
        /// FALLBACK path since TASK-353 — the concave mass now renders as a raw ear-clip with an ANALYTIC SDF in the wood
        /// shader (<c>_CONCAVE_SDF</c>); this dense refine only runs for outlines the shader's ≤64-vertex uniform cannot
        /// hold. (On a thin wall it produced 100k+ triangles and a multi-second load freeze — the reason it was demoted.)
        /// TASK-349: CONFORMING (T-junction-free) refinement of a triangulation so a per-vertex SDF can resolve the
        /// silhouette bands and every internal chord carries an interior vertex with distance &gt; 0 (which is what stops
        /// the wood shader painting an outline along the ear-clip chords). Red-green refinement: a triangle whose longest
        /// edge exceeds its target has all three edges split (RED, 1→4); a triangle that merely inherited a neighbour's
        /// split point is closed to conform (GREEN, 1→2 or 2→3). Edge midpoints are SHARED through a map keyed by the
        /// unordered vertex pair, so the two triangles across any edge use the SAME midpoint — no hanging node, hence no
        /// distance-field discontinuity (which would itself read as a new seam). The target is <paramref name="nearLen"/>
        /// for a triangle within <paramref name="bandDepth"/> of the boundary (fine, where the bands live) and
        /// <paramref name="farLen"/> in the deep interior (coarse, flat wood). Deterministic; bounded by
        /// <paramref name="maxVerts"/> and a pass guard (<paramref name="hitCeiling"/> ⇒ a faint chord ghost may survive).
        /// Unity-free; <paramref name="baseTris"/> indexes into <paramref name="boundary"/> (a simple polygon, its own ring).
        /// </summary>
        public static void RefineForSdf(Vec2[] boundary, int[] baseTris, float nearLen, float farLen, float bandDepth,
            int maxVerts, out Vec2[] outVerts, out int[] outTris, out bool hitCeiling)
        {
            var verts = new List<Vec2>(boundary.Length * 8);
            var dist = new List<float>(boundary.Length * 8);
            foreach (var v in boundary)
            {
                verts.Add(v);
                dist.Add(DistanceToPolygonBoundary(boundary, v)); // outline vertices ≈ 0
            }

            var tris = new List<int>(baseTris);
            var mid = new Dictionary<long, int>();

            int Midpoint(int i, int j)
            {
                var mkey = EdgeKey(i, j);
                if (mid.TryGetValue(mkey, out var m))
                    return m;
                m = verts.Count;
                var mp = (verts[i] + verts[j]) * 0.5f;
                verts.Add(mp);
                dist.Add(DistanceToPolygonBoundary(boundary, mp));
                mid[mkey] = m;
                return m;
            }

            bool HasMid(int i, int j, out int m) => mid.TryGetValue(EdgeKey(i, j), out m);

            var guard = 40;
            var changed = true;
            while (changed && guard-- > 0 && verts.Count < maxVerts)
            {
                changed = false;
                var next = new List<int>(tris.Count);
                for (var ti = 0; ti < tris.Count; ti += 3)
                {
                    int a = tris[ti], b = tris[ti + 1], c = tris[ti + 2];

                    // Size predicate: too big FOR ITS BAND ⇒ force all three edges to split (red).
                    var longest = MathF.Max((verts[b] - verts[a]).Length,
                                  MathF.Max((verts[c] - verts[b]).Length, (verts[a] - verts[c]).Length));
                    var dmin = MathF.Min(dist[a], MathF.Min(dist[b], dist[c]));
                    var target = dmin < bandDepth ? nearLen : farLen;
                    if (longest > target)
                    {
                        Midpoint(a, b);
                        Midpoint(b, c);
                        Midpoint(c, a);
                    }

                    var hAB = HasMid(a, b, out var pab);
                    var hBC = HasMid(b, c, out var pbc);
                    var hCA = HasMid(c, a, out var pca);
                    var k = (hAB ? 1 : 0) + (hBC ? 1 : 0) + (hCA ? 1 : 0);

                    if (k == 0)
                    {
                        next.Add(a); next.Add(b); next.Add(c);
                        continue;
                    }
                    changed = true;

                    if (k == 3)
                    {
                        Emit(next, a, pab, pca);
                        Emit(next, pab, b, pbc);
                        Emit(next, pca, pbc, c);
                        Emit(next, pab, pbc, pca);
                    }
                    else if (k == 1)
                    {
                        if (hAB) { Emit(next, a, pab, c); Emit(next, pab, b, c); }
                        else if (hBC) { Emit(next, b, pbc, a); Emit(next, pbc, c, a); }
                        else { Emit(next, c, pca, b); Emit(next, pca, a, b); }
                    }
                    else // k == 2 — two split edges, one plain corner between them
                    {
                        if (!hCA) { Emit(next, pab, b, pbc); Emit(next, a, pab, pbc); Emit(next, a, pbc, c); }
                        else if (!hAB) { Emit(next, pbc, c, pca); Emit(next, b, pbc, pca); Emit(next, b, pca, a); }
                        else { Emit(next, pca, a, pab); Emit(next, c, pca, pab); Emit(next, c, pab, b); }
                    }
                }
                tris = next;
            }

            hitCeiling = guard <= 0 || verts.Count >= maxVerts;
            outVerts = verts.ToArray();
            outTris = tris.ToArray();

            static long EdgeKey(int i, int j)
            {
                var lo = i < j ? i : j;
                var hi = i < j ? j : i;
                return ((long)lo << 32) | (uint)hi;
            }

            static void Emit(List<int> t, int a, int b, int c)
            {
                t.Add(a); t.Add(b); t.Add(c);
            }
        }

        /// <summary>Whether a simple polygon is convex: every cross product of consecutive edges shares one sign
        /// (collinear/zero allowed). Tolerant so an authored axis-aligned rectangle (exact) passes cleanly. TASK-346:
        /// promoted here (was <c>LevelDataValidator</c>'s private copy) so the validator's TASK-307 concavity gate and
        /// the view's convex-fan-vs-triangulator render dispatch decide "concave?" by the SAME test — they can never
        /// disagree on which static takes which path.</summary>
        public static bool IsConvex(Vec2[] v)
        {
            var n = v.Length;
            if (n < 3)
                return false;
            var sign = 0;
            for (var i = 0; i < n; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % n];
                var c = v[(i + 2) % n];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (cross > 1e-6f)
                {
                    if (sign < 0) return false;
                    sign = 1;
                }
                else if (cross < -1e-6f)
                {
                    if (sign > 0) return false;
                    sign = -1;
                }
            }
            return true;
        }

        /// <summary>
        /// TASK-346: ear-clipping triangulation of a SIMPLE polygon — fills a CONCAVE outline that the centroid fan of
        /// <c>PieceMeshBuilder.Build</c> cannot (its centroid can fall in a notch and invert triangles). The outline
        /// must be simple (no self-intersection, no degenerate edges) — the static-geometry validator enforces exactly
        /// that before a concave visual-only static reaches here. Winding is normalised to CCW internally, so
        /// <paramref name="triangles"/> is emitted CCW regardless of authored winding and indexes into
        /// <paramref name="verts"/> (the CCW-normalised outline). Returns false — with <paramref name="triangles"/> null
        /// — if no ear is found in a full pass (a non-simple contour slipped through): the caller draws a safe fallback
        /// and logs, so a malformed contour never hangs the clip loop nor yields a silently-wrong mesh. O(n²), Unity-free,
        /// deterministic (no float-order-dependent branch — the same outline always clips to the same triangles).
        /// </summary>
        public static bool TriangulateConcave(Vec2[] outline, out Vec2[] verts, out int[] triangles)
        {
            triangles = null;
            verts = null;
            if (outline == null || outline.Length < 3)
                return false;

            var n = outline.Length;
            verts = new Vec2[n];
            // Normalise to CCW so the ear test (convex tip + empty triangle) has a consistent interior side.
            if (PolygonSignedArea(outline) >= 0f)
                Array.Copy(outline, verts, n);
            else
                for (var i = 0; i < n; i++)
                    verts[i] = outline[n - 1 - i];

            var indices = new List<int>(n);
            for (var i = 0; i < n; i++)
                indices.Add(i);

            var tris = new List<int>((n - 2) * 3);
            var guard = n * n; // O(n²) ceiling: a simple polygon always clips within this; a bad one bails, never hangs
            while (indices.Count > 3 && guard-- > 0)
            {
                var earFound = false;
                var m = indices.Count;
                for (var i = 0; i < m; i++)
                {
                    var i0 = indices[(i - 1 + m) % m];
                    var i1 = indices[i];
                    var i2 = indices[(i + 1) % m];
                    var a = verts[i0];
                    var b = verts[i1];
                    var c = verts[i2];

                    if (TriArea2(a, b, c) <= 0f)
                        continue; // reflex or collinear tip — never an ear on a CCW ring

                    var empty = true;
                    for (var j = 0; j < m; j++)
                    {
                        var ij = indices[j];
                        if (ij == i0 || ij == i1 || ij == i2)
                            continue;
                        if (PointInTriangle(verts[ij], a, b, c))
                        {
                            empty = false;
                            break;
                        }
                    }
                    if (!empty)
                        continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                    return false; // no ear in a full pass ⇒ not a simple polygon
            }

            if (indices.Count != 3)
                return false;
            tris.Add(indices[0]); tris.Add(indices[1]); tris.Add(indices[2]);
            triangles = tris.ToArray();
            return true;
        }

        /// <summary>Signed area of a polygon ring (shoelace ×½): &gt;0 CCW, &lt;0 CW.</summary>
        private static float PolygonSignedArea(Vec2[] v)
        {
            var sum = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                sum += v[j].X * v[i].Y - v[i].X * v[j].Y;
            return sum * 0.5f;
        }

        /// <summary>Twice the signed area of triangle a-b-c: &gt;0 for a CCW (left) turn, &lt;0 for CW, 0 collinear.</summary>
        private static float TriArea2(Vec2 a, Vec2 b, Vec2 c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        /// <summary>Point-in-triangle by edge-sign consistency; a point ON an edge counts as inside, so a vertex lying
        /// on a candidate ear's boundary correctly BLOCKS that ear (keeps the clip from emitting an overlapping tri).</summary>
        private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            var d1 = TriArea2(a, b, p);
            var d2 = TriArea2(b, c, p);
            var d3 = TriArea2(c, a, p);
            var hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            var hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
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
