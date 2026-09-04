using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-325 (S2) scene-free geometry tests: the pure ribbon builder (<see cref="Geometry.SurfaceRibbonQuads"/>)
    /// and the static-geometry validator gates (self-intersection, vertex count, degenerate edges, the TASK-307
    /// concave-filled-solid gate and the surface exemption). Everything here is deterministic Unity-free domain code,
    /// so it runs in EditMode without a Unity scene — exactly the code the owner named as testable in minutes.
    /// The validator is exercised through its PUBLIC entry (<see cref="LevelDataValidator.Validate"/>): a fixture sets
    /// only <c>StaticGeometry</c>, so the other checks add their own (irrelevant) errors while we assert on the
    /// static-geometry message substrings alone.
    /// </summary>
    [TestFixture]
    public sealed class StaticGeometryTests
    {
        // ---------------------------------------------------------------- Geometry.SurfaceRibbonQuads

        [Test]
        public void SurfaceRibbonQuads_FewerThanTwoPoints_IsEmpty()
        {
            Assert.That(Geometry.SurfaceRibbonQuads(null, 0.2f), Is.Empty);
            Assert.That(Geometry.SurfaceRibbonQuads(new[] { new Vec2(0f, 0f) }, 0.2f), Is.Empty);
        }

        [Test]
        public void SurfaceRibbonQuads_NPoints_YieldsNMinusOneConvexQuads()
        {
            var polyline = new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(2f, 1f), new Vec2(3f, 1f) };
            var quads = Geometry.SurfaceRibbonQuads(polyline, 0.2f);

            Assert.That(quads.Length, Is.EqualTo(polyline.Length - 1), "one quad per segment");
            foreach (var quad in quads)
            {
                Assert.That(quad.Length, Is.EqualTo(4), "each ribbon quad is a 4-vertex box");
                Assert.That(SignedArea(quad), Is.GreaterThan(0f), "each quad is wound CCW (positive area)");
            }
        }

        [Test]
        public void SurfaceRibbonQuads_HorizontalSegment_OffsetsHalfThicknessAlongNormal()
        {
            // A single horizontal segment 0→2 with a 0.2 ribbon: corners sit at y = ±0.1 (half the thickness),
            // spanning the full x extent. Proves the offset is EXACTLY half-thickness along the segment normal.
            var quad = Geometry.SurfaceRibbonQuads(new[] { new Vec2(0f, 0f), new Vec2(2f, 0f) }, 0.2f).Single();

            Assert.That(quad.Min(v => v.Y), Is.EqualTo(-0.1f).Within(1e-5f));
            Assert.That(quad.Max(v => v.Y), Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(quad.Min(v => v.X), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(quad.Max(v => v.X), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void SurfaceRibbonQuads_NonPositiveThickness_FallsBackToDegeneracyFloor()
        {
            // Thickness 0 must not collapse the ribbon to a line — it floors at 1e-4 (half = 5e-5), so the quad
            // still has non-zero area (the degeneracy of the *segment* is the validator's job, not the builder's).
            var quad = Geometry.SurfaceRibbonQuads(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f) }, 0f).Single();
            Assert.That(SignedArea(quad), Is.GreaterThan(0f));
        }

        // ---------------------------------------------------------------- ValidateStaticGeometry gates

        [Test]
        public void Validate_ConvexFilledSolid_HasNoStaticGeometryError()
        {
            // A clean convex CCW triangle: none of the static-geometry gates should fire — this is the corpus case
            // (every current static is a small convex block), so the new validator must reject nothing today.
            var result = ValidateStatic(Filled(new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 2f)));
            Assert.That(StaticErrors(result), Is.Empty);
        }

        [Test]
        public void Validate_ConcaveFilledSolid_RejectedByTask307Gate()
        {
            // A concave (arrow-notch) filled solid — Box2D would split it coordinate-dependently. TASK-307 gate.
            var result = ValidateStatic(Filled(
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f), new Vec2(2f, 2f), new Vec2(0f, 2f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("CONCAVE filled solid"));
        }

        [Test]
        public void Validate_SelfIntersectingFilledRing_Rejected()
        {
            // A bow-tie ring (edges cross) is not a simple contour.
            var result = ValidateStatic(Filled(
                new Vec2(0f, 0f), new Vec2(2f, 2f), new Vec2(2f, 0f), new Vec2(0f, 2f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("self-intersects"));
        }

        [Test]
        public void Validate_DegenerateEdge_Rejected()
        {
            // A duplicated point makes a zero-length edge — a degeneracy, flagged before the collider is built.
            var result = ValidateStatic(Filled(
                new Vec2(0f, 0f), new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 2f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("degenerate"));
        }

        [Test]
        public void Validate_FilledSolidWithTooFewVertices_Rejected()
        {
            var result = ValidateStatic(Filled(new Vec2(0f, 0f), new Vec2(1f, 0f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("vertices"));
        }

        // ---------------------------------------------------------------- surface path (S2)

        [Test]
        public void Validate_ConcaveSurface_IsExemptFromTask307Gate()
        {
            // A concave OPEN polyline surface (a chute lip) — the core of the task. An edge chain carries no convex
            // split, so the concavity gate must NOT fire on a surface; only the filled-solid gate does.
            var result = ValidateStatic(Surface(0.2f,
                new Vec2(-5f, 0f), new Vec2(-5f, 4.5f), new Vec2(-3f, 1f), new Vec2(-1f, 0f)));
            Assert.That(StaticErrors(result), Has.None.Contains("CONCAVE filled solid"));
        }

        [Test]
        public void Validate_SelfIntersectingSurface_Rejected()
        {
            var result = ValidateStatic(Surface(0.2f,
                new Vec2(0f, 0f), new Vec2(2f, 2f), new Vec2(2f, 0f), new Vec2(0f, 2f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("self-intersects"));
        }

        [Test]
        public void Validate_SurfaceWithOnePoint_Rejected()
        {
            var result = ValidateStatic(Surface(0.2f, new Vec2(0f, 0f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("vertices"));
        }

        // ---------------------------------------------------------------- TASK-346: visual-only concave fill

        [Test]
        public void Validate_ConcaveVisualOnlyFill_IsExemptFromTask307Gate()
        {
            // The core of TASK-346: a CONCAVE outline with collider:false builds NO collider, so there is no Box2D split
            // to diverge — the concavity gate must NOT fire. (The same arrow-notch shape that IS rejected with a collider.)
            var result = ValidateStatic(VisualOnly(
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f), new Vec2(2f, 2f), new Vec2(0f, 2f)));
            Assert.That(StaticErrors(result), Has.None.Contains("CONCAVE filled solid"));
        }

        [Test]
        public void Validate_ConcaveVisualOnlyFill_StillRejectsSelfIntersection()
        {
            // collider:false exempts ONLY the concavity gate — the triangulator still needs a simple polygon, so the
            // self-intersection check must keep firing (a bow-tie ring cannot be ear-clipped).
            var result = ValidateStatic(VisualOnly(
                new Vec2(0f, 0f), new Vec2(2f, 2f), new Vec2(2f, 0f), new Vec2(0f, 2f)));
            Assert.That(StaticErrors(result), Has.Some.Contains("self-intersects"));
        }

        [Test]
        public void Polygon_HasCollider_DefaultsTrueAndHonoursTheFlag()
        {
            Assert.That(new Polygon(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(0f, 1f) }).HasCollider, Is.True);
            Assert.That(VisualOnly(new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(0f, 1f)).HasCollider, Is.False);
        }

        // ---------------------------------------------------------------- Geometry.IsConvex / TriangulateConcave

        [Test]
        public void IsConvex_TrueForConvex_FalseForConcave()
        {
            Assert.That(Geometry.IsConvex(new[] { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 2f), new Vec2(0f, 2f) }), Is.True);
            Assert.That(Geometry.IsConvex(new[]
            {
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f), new Vec2(2f, 2f), new Vec2(0f, 2f)
            }), Is.False, "the arrow-notch is concave");
        }

        [Test]
        public void TriangulateConcave_TilesTheConcavePolygon_NMinusTwoTrianglesCoveringItsArea()
        {
            // An arrow-notch (concave) ring: ear clipping must succeed, emit exactly n-2 triangles, and those triangles
            // must tile the polygon — their total area equals the polygon's shoelace area (no gaps, no overlaps).
            var outline = new[]
            {
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f), new Vec2(2f, 2f), new Vec2(0f, 2f)
            };
            Assert.That(Geometry.TriangulateConcave(outline, out var verts, out var tris), Is.True);
            Assert.That(tris.Length, Is.EqualTo((outline.Length - 2) * 3), "n-2 triangles for an n-gon");

            var triArea = 0f;
            for (var i = 0; i < tris.Length; i += 3)
                triArea += TriangleArea(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]);
            Assert.That(triArea, Is.EqualTo(System.Math.Abs(Shoelace(outline))).Within(1e-4f));
        }

        [Test]
        public void TriangulateConcave_NormalisesToCcw_EmittingCcwTriangles()
        {
            // A CLOCKWISE concave ring must still triangulate — the clip normalises winding, so every emitted triangle
            // is CCW (positive signed area). This is why authored fill winding does not matter.
            var cw = new[]
            {
                new Vec2(0f, 2f), new Vec2(2f, 2f), new Vec2(1f, 1f), new Vec2(2f, 0f), new Vec2(0f, 0f)
            };
            Assert.That(Geometry.TriangulateConcave(cw, out var verts, out var tris), Is.True);
            for (var i = 0; i < tris.Length; i += 3)
                Assert.That(TriangleSignedArea(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]),
                    Is.GreaterThan(0f), "each emitted triangle is CCW");
        }

        [Test]
        public void TriangulateConcave_DegenerateInput_FailsCleanly()
        {
            Assert.That(Geometry.TriangulateConcave(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f) }, out _, out var tris), Is.False);
            Assert.That(tris, Is.Null);
        }

        // ---------------------------------------------------------------- TASK-349: NearestBoundaryEdge

        [Test]
        public void NearestBoundaryEdge_ReturnsSameDistanceAsBoundaryHelper_AndAValidEdge()
        {
            var poly = new[] { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };
            foreach (var p in new[] { new Vec2(1f, 0.4f), new Vec2(3.7f, 1.5f), new Vec2(2f, 1.5f), new Vec2(0.2f, 2.9f) })
            {
                var d = Geometry.NearestBoundaryEdge(poly, p, out var e);
                Assert.That(e, Is.InRange(0, poly.Length - 1), "edge index is a real edge");
                Assert.That(d, Is.EqualTo(Geometry.DistanceToPolygonBoundary(poly, p)).Within(1e-4f),
                    "distance agrees with the boundary-distance helper (same closest-point pass)");
            }

            // A point just inside the bottom edge picks edge 0 (v0→v1, the bottom) with distance ≈ its height.
            var dist = Geometry.NearestBoundaryEdge(poly, new Vec2(2f, 0.25f), out var edge);
            Assert.That(edge, Is.EqualTo(0));
            Assert.That(dist, Is.EqualTo(0.25f).Within(1e-4f));
        }

        // ---------------------------------------------------------------- TASK-349: SurfaceRibbonStrip

        [Test]
        public void SurfaceRibbonStrip_FewerThanTwoPoints_IsEmpty()
        {
            Assert.That(Geometry.SurfaceRibbonStrip(null, 0.2f).IsEmpty, Is.True);
            Assert.That(Geometry.SurfaceRibbonStrip(new[] { new Vec2(0f, 0f) }, 0.2f).IsEmpty, Is.True);
        }

        [Test]
        public void SurfaceRibbonStrip_ThreeRowsPerPoint_CrossSpansPlusMinusHalfWithCentre()
        {
            var polyline = new[] { new Vec2(0f, 0f), new Vec2(1f, 0.2f), new Vec2(2f, 0f) };
            var strip = Geometry.SurfaceRibbonStrip(polyline, 0.2f);

            Assert.That(strip.Half, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(strip.Vertices.Length, Is.EqualTo(3 * polyline.Length), "left rail / centre / right rail per point");
            Assert.That(strip.Triangles.Length, Is.EqualTo((polyline.Length - 1) * 12), "two quad columns per segment");
            for (var i = 0; i < polyline.Length; i++)
            {
                Assert.That(strip.Cross[3 * i + 0], Is.EqualTo(+strip.Half).Within(1e-6f), "left rail = +half");
                Assert.That(strip.Cross[3 * i + 1], Is.EqualTo(0f).Within(1e-6f), "centreline = 0");
                Assert.That(strip.Cross[3 * i + 2], Is.EqualTo(-strip.Half).Within(1e-6f), "right rail = −half");
            }
            foreach (var t in strip.Triangles)
                Assert.That(t, Is.InRange(0, strip.Vertices.Length - 1), "every index references a real vertex");
        }

        [Test]
        public void SurfaceRibbonStrip_StraightLine_RailsMatchSurfaceRibbonQuadsOffset()
        {
            // On a straight polyline the miter normal equals the segment normal, so the strip rails coincide with the
            // per-quad outer edges: left rail at +half, centreline at 0, right rail at −half (the drawn≈measured invariant).
            var polyline = new[] { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(4f, 0f) };
            var strip = Geometry.SurfaceRibbonStrip(polyline, 0.2f);
            for (var i = 0; i < polyline.Length; i++)
            {
                Assert.That(strip.Vertices[3 * i + 0].Y, Is.EqualTo(+0.1f).Within(1e-5f));
                Assert.That(strip.Vertices[3 * i + 1].Y, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(strip.Vertices[3 * i + 2].Y, Is.EqualTo(-0.1f).Within(1e-5f));
                Assert.That(strip.Vertices[3 * i + 1].X, Is.EqualTo(polyline[i].X).Within(1e-5f));
            }
        }

        // ---------------------------------------------------------------- TASK-349: RefineForSdf (conforming)

        [Test]
        public void RefineForSdf_ConformingNoHangingNodes_PreservesAreaAndRefinesToTarget()
        {
            // The arrow-notch (concave) again: ear-clip it, then refine to edge ≤ 0.5 everywhere. The refined mesh must
            // (a) tile the SAME area, (b) contain NO T-junction (no vertex sitting on another triangle's open edge —
            // that is what would re-introduce an SDF seam), and (c) actually reach the target edge length.
            var outline = new[]
            {
                new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f), new Vec2(2f, 2f), new Vec2(0f, 2f)
            };
            Assert.That(Geometry.TriangulateConcave(outline, out var boundary, out var baseTris), Is.True);

            const float target = 0.5f;
            Geometry.RefineForSdf(boundary, baseTris, target, target, bandDepth: 100f, maxVerts: 100000,
                out var verts, out var tris, out var hitCeiling);

            Assert.That(hitCeiling, Is.False, "the arrow refines well within the ceiling");
            Assert.That(tris.Length % 3, Is.EqualTo(0));

            // (a) area preserved
            var area = 0f;
            for (var i = 0; i < tris.Length; i += 3)
                area += TriangleArea(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]);
            Assert.That(area, Is.EqualTo(System.Math.Abs(Shoelace(outline))).Within(1e-3f), "refined mesh tiles the polygon");

            // (c) every edge reached the target
            for (var i = 0; i < tris.Length; i += 3)
            {
                var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
                Assert.That((b - a).Length, Is.LessThanOrEqualTo(target + 1e-3f));
                Assert.That((c - b).Length, Is.LessThanOrEqualTo(target + 1e-3f));
                Assert.That((a - c).Length, Is.LessThanOrEqualTo(target + 1e-3f));
            }

            // (b) conformity — no vertex strictly interior to a triangle edge it does not belong to
            for (var ti = 0; ti < tris.Length; ti += 3)
            {
                int x = tris[ti], y = tris[ti + 1], z = tris[ti + 2];
                for (var vi = 0; vi < verts.Length; vi++)
                {
                    if (vi == x || vi == y || vi == z)
                        continue;
                    Assert.That(OnOpenSegment(verts[vi], verts[x], verts[y]), Is.False, "T-junction on edge xy");
                    Assert.That(OnOpenSegment(verts[vi], verts[y], verts[z]), Is.False, "T-junction on edge yz");
                    Assert.That(OnOpenSegment(verts[vi], verts[z], verts[x]), Is.False, "T-junction on edge zx");
                }
            }
        }

        private static bool OnOpenSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (System.Math.Abs(cross) > 1e-4f)
                return false; // not collinear
            var dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            var len2 = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            return dot > 1e-5f && dot < len2 - 1e-5f; // strictly between the endpoints
        }

        // ---------------------------------------------------------------- helpers

        private static Polygon Filled(params Vec2[] outline) => new Polygon(outline);

        private static Polygon Surface(float thickness, params Vec2[] outline)
            => new Polygon(outline, null, new StaticSurface(thickness));

        private static Polygon VisualOnly(params Vec2[] outline)
            => new Polygon(outline, null, null, false);

        private static float Shoelace(Vec2[] poly)
        {
            var sum = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                sum += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return sum * 0.5f;
        }

        private static float TriangleSignedArea(Vec2 a, Vec2 b, Vec2 c)
            => ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;

        private static float TriangleArea(Vec2 a, Vec2 b, Vec2 c)
            => System.Math.Abs(TriangleSignedArea(a, b, c));

        private static ValidationResult ValidateStatic(Polygon block)
            => new LevelDataValidator().Validate(new LevelData { StaticGeometry = new[] { block } });

        // The static-geometry gates all name "StaticGeometry[" in their message; the fixture's other (irrelevant)
        // errors — null target/pieces — never do, so this isolates the checks under test.
        private static string[] StaticErrors(ValidationResult result)
            => result.Errors.Where(e => e.Contains("StaticGeometry[")).ToArray();

        private static float SignedArea(Vec2[] poly)
        {
            var sum = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                sum += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return sum * 0.5f;
        }
    }
}
