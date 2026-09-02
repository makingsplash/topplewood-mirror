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

        // ---------------------------------------------------------------- helpers

        private static Polygon Filled(params Vec2[] outline) => new Polygon(outline);

        private static Polygon Surface(float thickness, params Vec2[] outline)
            => new Polygon(outline, null, new StaticSurface(thickness));

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
