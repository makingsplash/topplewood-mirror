using Gameplay.Core;
using Gameplay.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-077: the honesty detector's per-pair baseline from the solver's "solved" criterion
    /// (<see cref="ContactRestAllowance"/>). Penetrations are produced by the REAL <see cref="Geometry.Penetration"/>
    /// on native discs / polygons placed at a chosen depth, and the overshoot is <c>raw − Baseline</c> — the same
    /// arithmetic SimRun.ComputeHonestyMetrics does — so a test fails when the baseline OR the detector geometry moves.
    /// Gate thresholds mirror <c>sim-gate.py</c> (<c>PEN_EPS = 0.005</c>, exclusive) and
    /// <see cref="SimRun.DetectorEpsilon"/>. Depths sit ±1e-4 off the boundary on purpose: float rounding of
    /// <c>raw − 0.01</c> at exactly 0.005 is not a property the gate promises. Physics-free (T4 reads one setting).
    /// </summary>
    [TestFixture]
    public sealed class ContactRestAllowanceTests
    {
        private const float PenEps = 0.005f; // sim-gate.py PEN_EPS
        private const float R = 0.5f;

        private static ConvexShape Disc(float x, float y = 0f, float r = R) => ConvexShape.Circle(new Vec2(x, y), r);

        private static ConvexShape Box(float cx, float cy, float halfW, float halfH) => ConvexShape.Polygon(new[]
        {
            new Vec2(cx - halfW, cy - halfH), new Vec2(cx + halfW, cy - halfH),
            new Vec2(cx + halfW, cy + halfH), new Vec2(cx - halfW, cy + halfH),
        });

        /// <summary>Two discs of radius R whose surfaces overlap by <paramref name="depth"/>.</summary>
        private static (ConvexShape A, ConvexShape B) DiscPairAtDepth(float depth) => (Disc(0f), Disc(2f * R - depth));

        private static float Overshoot(in ConvexShape a, in ConvexShape b, float staticOverlap)
            => Geometry.Penetration(a, b) - ContactRestAllowance.Baseline(a, b, staticOverlap);

        // ---------------------------------------------------------------- T1 circle–circle

        [Test]
        public void T1_CircleCircle_BaselineIsTwoSlop()
        {
            var (a, b) = DiscPairAtDepth(0.012f);
            Assert.That(ContactRestAllowance.Baseline(a, b, 0f), Is.EqualTo(0.01f).Within(1e-7f));
        }

        [Test]
        public void T1_CircleCircle_SolvedDepthPasses_DeeperFails()
        {
            var (a, b) = DiscPairAtDepth(0.0149f);
            Assert.That(Geometry.Penetration(a, b), Is.EqualTo(0.0149f).Within(1e-5f), "fixture depth");
            Assert.That(Overshoot(a, b, 0f), Is.LessThanOrEqualTo(PenEps), "0.0149 is a solved contact → no pen-on-win");

            (a, b) = DiscPairAtDepth(0.0151f);
            Assert.That(Overshoot(a, b, 0f), Is.GreaterThan(PenEps), "0.0151 is past 3·slop → pen-on-win");
        }

        [Test]
        public void T1_CircleCircle_ShaftSortWorstHonestWin_Passes_And_VideoLauncherReverse_Passes()
        {
            // Measured honest rests (TASK-077 02 §3–§5): shaft_sort main worst pair 0.01075, the reordered twin
            // 0.01237, video_launcher reverse order 0.0099 — all inside the solver's 0.015.
            foreach (var depth in new[] { 0.0099f, 0.01075f, 0.01237f })
            {
                var (a, b) = DiscPairAtDepth(depth);
                Assert.That(Overshoot(a, b, 0f), Is.LessThanOrEqualTo(PenEps), $"depth {depth}");
            }
        }

        // ---------------------------------------------------------------- T2 pairs with a polygon

        [Test]
        public void T2_CirclePolygon_BaselineZero_SameAsBefore()
        {
            var disc = Disc(0f, 0.5f);
            var floor = Box(0f, -0.5f, 2f, 0.5f);
            Assert.That(ContactRestAllowance.Baseline(disc, floor, 0f), Is.EqualTo(0f));
            Assert.That(ContactRestAllowance.Baseline(floor, disc, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void T2_CirclePolygon_PenetrationAboveSlopStillFails()
        {
            var disc = Disc(0f, 0.5f - 0.0051f); // sunk 0.0051 into the box top at y = 0
            var floor = Box(0f, -0.5f, 2f, 0.5f);
            Assert.That(Overshoot(disc, floor, 0f), Is.GreaterThan(PenEps));
        }

        [Test]
        public void T2_PolygonPolygon_And_Compound_BaselineZero()
        {
            var boxA = Box(0f, 0f, 0.5f, 0.5f);
            var boxB = Box(0.99f, 0f, 0.5f, 0.5f);
            Assert.That(ContactRestAllowance.Baseline(boxA, boxB, 0f), Is.EqualTo(0f));

            var compound = ConvexShape.Compound(new[]
            {
                new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f), new Vec2(0f, 1f) },
                new[] { new Vec2(1f, 0f), new Vec2(2f, 0f), new Vec2(2f, 1f), new Vec2(1f, 1f) },
            });
            Assert.That(ContactRestAllowance.SkinOf(compound), Is.EqualTo(ContactRestAllowance.PolygonSkin));
            Assert.That(ContactRestAllowance.Baseline(compound, Disc(3f), 0f), Is.EqualTo(0f));
            Assert.That(ContactRestAllowance.Baseline(compound, boxA, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void T2_StaticOverlapOnPolygonPairIsKeptVerbatim()
        {
            var boxA = Box(0f, 0f, 0.5f, 0.5f);
            var boxB = Box(0.99f, 0f, 0.5f, 0.5f);
            Assert.That(ContactRestAllowance.Baseline(boxA, boxB, 0.0123f), Is.EqualTo(0.0123f));
        }

        // ---------------------------------------------------------------- T3 max with the authored overlap

        [Test]
        public void T3_CircleCircle_DeeperAuthoredNestingKeepsItsBaseline()
        {
            var (a, b) = DiscPairAtDepth(0.03f);
            Assert.That(ContactRestAllowance.Baseline(a, b, 0.03f), Is.EqualTo(0.03f));
        }

        [Test]
        public void T3_CircleCircle_ShallowAuthoredOverlapIsRaisedToAllowance()
        {
            var (a, b) = DiscPairAtDepth(0.006f);
            Assert.That(ContactRestAllowance.Baseline(a, b, 0.006f), Is.EqualTo(0.01f).Within(1e-7f));
        }

        // ---------------------------------------------------------------- T4 settings drift

        [Test]
        public void T4_PolygonSkinMatchesProjectDefaultContactOffset()
        {
            Assert.That(Physics2D.defaultContactOffset, Is.EqualTo(ContactRestAllowance.PolygonSkin).Within(1e-7f),
                "Physics2DSettings.m_DefaultContactOffset changed — the solver allowance in ContactRestAllowance " +
                "is derived from it; re-derive the limit (sim-harness-metrics.md) instead of editing this test.");
        }

        [Test]
        public void T4_ConstantsKeepTheSolverIdentity()
        {
            Assert.That(ContactRestAllowance.SolvedPenetration, Is.EqualTo(3f * ContactRestAllowance.LinearSlop));
            Assert.That(ContactRestAllowance.RestAllowance,
                Is.EqualTo(ContactRestAllowance.SolvedPenetration - ContactRestAllowance.GateMargin).Within(1e-7f));
            Assert.That(ContactRestAllowance.GateMargin, Is.EqualTo(PenEps), "must equal sim-gate.py PEN_EPS");
        }

        // ---------------------------------------------------------------- T5 dishonesty stays FAIL

        [Test]
        public void T5_CircleCircle_DeepOverlapCountsAsIntersecting()
        {
            var (a, b) = DiscPairAtDepth(0.061f);
            Assert.That(Overshoot(a, b, 0f), Is.GreaterThan(SimRun.DetectorEpsilon), "0.061 → IntersectingPairs +1");

            (a, b) = DiscPairAtDepth(0.0151f);
            var overshoot = Overshoot(a, b, 0f);
            Assert.That(overshoot, Is.GreaterThan(PenEps), "0.0151 → pen-on-win");
            Assert.That(overshoot, Is.LessThanOrEqualTo(SimRun.DetectorEpsilon), "…but not an intersect");
        }
    }
}
