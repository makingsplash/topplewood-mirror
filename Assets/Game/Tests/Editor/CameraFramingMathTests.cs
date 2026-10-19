using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-442 scene-free tests of <see cref="CameraFramingMath.TryContentBounds"/>: the DYNAMIC content (pieces,
    /// slots, air zones with the fan reserve, surface strips in full) sets the frame box; closed static blocks only
    /// extend it vertically, and only with the part of each outline edge clipped to that X span. Fixtures are built from domain types in code
    /// (no manifest). <see cref="WideFloor_DoesNotWidenBounds"/> is the test that fails first on the pre-TASK-442 body
    /// (there min.X was the floor's −7).
    /// </summary>
    [TestFixture]
    public sealed class CameraFramingMathTests
    {
        private const float Eps = 1e-4f;
        private static readonly MaterialProfile Wood = new MaterialProfile(1.0f, 0.6f, 0.05f);

        // ---------------------------------------------------------------- wide floor

        [Test]
        public void WideFloor_DoesNotWidenBounds()
        {
            // Two 0.5 boxes at x = ±0.75 → dynamic X span [-1, 1]; a ±7 floor under them.
            var level = Level(Pieces((-0.75f, 2f), (0.75f, 2f)), Filled(-7f, -0.6f, 7f, 0f));

            Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out var max), Is.True);
            Assert.That(min.X, Is.EqualTo(-1f).Within(Eps), "the floor must not widen the box to x = -7");
            Assert.That(max.X, Is.EqualTo(1f).Within(Eps));
            Assert.That(min.Y, Is.EqualTo(-0.6f).Within(Eps), "the floor under the figure still counts in Y");
            Assert.That(max.Y, Is.EqualTo(2.25f).Within(Eps));
        }

        [Test]
        public void WideFloor_OrthoSizeIsHeightBound()
        {
            // Dynamic X span [-0.5, 0.5], Y [1.75, 2.25]; floor ±7. At aspect 0.45 the old box was width-bound
            // (7 / 0.45 + margin); the new one is height-bound.
            const float aspect = 0.45f, margin = 0.6f, band = 0.2f;
            var level = Level(Pieces((-0.25f, 2f), (0.25f, 2f)), Filled(-7f, -0.6f, 7f, 0f));

            Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out var max), Is.True);
            var size = CameraFramingMath.ComputeOrthoSize(min, max, aspect, margin, band, 0f);

            var sizeForHeight = ((2.25f - -0.6f) * 0.5f + margin) / (1f - band);   // 2.53125
            var oldSize = 7f / aspect + margin;                                        // 16.16 (width-bound)
            Assert.That(size, Is.EqualTo(sizeForHeight).Within(Eps));
            Assert.That(size, Is.LessThan(oldSize));
        }

        // ---------------------------------------------------------------- clipping to the dynamic span

        [Test]
        public void StaticOutsideSlab_IgnoredEntirely()
        {
            var pieces = Pieces((-0.75f, 2f), (0.75f, 2f));
            Assert.That(CameraFramingMath.TryContentBounds(Level(pieces), out var bareMin, out var bareMax), Is.True);

            // A tall wall entirely right of the span (x ∈ [3, 4], up to y = 20) changes nothing.
            var withWall = Level(pieces, Filled(3f, 0f, 4f, 20f));
            Assert.That(CameraFramingMath.TryContentBounds(withWall, out var min, out var max), Is.True);
            AssertVec(min, bareMin);
            AssertVec(max, bareMax);
        }

        [Test]
        public void SlantedStatic_ClippedAtSlabEdge_YInterpolated()
        {
            // Closed sliver (-5,0)→(5,5); span [-1, 1] ⇒ its in-span Y range is [2.0, 3.0], not [0, 5].
            var ramp = Sliver(new Vec2(-5f, 0f), new Vec2(5f, 5f));

            var high = Level(Pieces((-0.75f, 10f), (0.75f, 10f)), ramp);
            Assert.That(CameraFramingMath.TryContentBounds(high, out var minHigh, out _), Is.True);
            Assert.That(minHigh.Y, Is.EqualTo(2f).Within(Eps), "low end interpolated at x = -1");

            var low = Level(Pieces((-0.75f, -10f), (0.75f, -10f)), ramp);
            Assert.That(CameraFramingMath.TryContentBounds(low, out _, out var maxLow), Is.True);
            Assert.That(maxLow.Y, Is.EqualTo(3f).Within(Eps), "high end interpolated at x = +1");
        }

        [Test]
        public void ClosedPolygon_ClosingEdgeCounts()
        {
            // Outline whose only in-span edge at y = -3 is the CLOSING edge (5,-3)→(-5,-3).
            var outline = new[] { new Vec2(-5f, -3f), new Vec2(-5f, 5f), new Vec2(5f, 5f), new Vec2(5f, -3f) };
            var pieces = Pieces((-0.75f, 0f), (0.75f, 0f));

            Assert.That(CameraFramingMath.TryContentBounds(Level(pieces, new Polygon(outline)), out var min, out var max),
                Is.True);
            Assert.That(min.Y, Is.EqualTo(-3f).Within(Eps), "closed block: the closing edge is part of the outline");
            Assert.That(max.Y, Is.EqualTo(5f).Within(Eps));
            Assert.That(min.X, Is.EqualTo(-1f).Within(Eps), "a closed block never widens the box");
        }

        [Test]
        public void VerticalEdge_OnSlabBoundary_Included_JustOutside_Not()
        {
            var pieces = Pieces((-0.75f, 2f), (0.75f, 2f));   // span [-1, 1]

            var onEdge = Level(pieces, Sliver(new Vec2(1f, -5f), new Vec2(1f, -4f)));
            Assert.That(CameraFramingMath.TryContentBounds(onEdge, out var minOn, out _), Is.True);
            Assert.That(minOn.Y, Is.EqualTo(-5f).Within(Eps));

            var outside = Level(pieces, Sliver(new Vec2(1.001f, -5f), new Vec2(1.001f, -4f)));
            Assert.That(CameraFramingMath.TryContentBounds(outside, out var minOut, out _), Is.True);
            Assert.That(minOut.Y, Is.EqualTo(1.75f).Within(Eps));
        }

        // ---------------------------------------------------------------- surface strip = the piece's path

        [Test]
        public void SurfaceStrip_OutsideSpan_EntersWhole_AndWidensTheStaticSpan()
        {
            // Pieces span x [-1, 1], y [1.75, 2.25]. A chute (-6,3)→(-4,0) lies wholly left of that span (the
            // chute_cross case). It is the piece's path, so it enters in X and Y; the floor under it (x ∈ [-7, -3],
            // also outside the original span) then counts in Y within the WIDENED span; a wall right of it does not.
            var chute = Surface(new Vec2(-6f, 3f), new Vec2(-4f, 0f));
            var floorUnderChute = Filled(-7f, -2f, -3f, -1.5f);
            var wall = Filled(3f, 0f, 4f, 20f);

            var level = Level(Pieces((-0.75f, 2f), (0.75f, 2f)), chute, floorUnderChute, wall);
            Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out var max), Is.True);
            Assert.That(min.X, Is.EqualTo(-6f).Within(Eps), "the chute widens the box in X");
            Assert.That(max.Y, Is.EqualTo(3f).Within(Eps), "the chute's top counts in Y");
            Assert.That(min.Y, Is.EqualTo(-2f).Within(Eps), "the floor under the chute counts in the widened span");
            Assert.That(max.X, Is.EqualTo(1f).Within(Eps), "closed statics still never widen the box");
        }

        // ---------------------------------------------------------------- fallback and dynamic reserve

        [Test]
        public void NoDynamic_FallsBackToFullStatic()
        {
            var staticOnly = Level(null, Filled(-7f, -0.6f, 7f, 0f), Filled(3f, 0f, 4f, 20f));
            Assert.That(CameraFramingMath.TryContentBounds(staticOnly, out var min, out var max), Is.True);
            AssertVec(min, new Vec2(-7f, -0.6f));
            AssertVec(max, new Vec2(7f, 20f));

            Assert.That(CameraFramingMath.TryContentBounds(new LevelData(), out _, out _), Is.False);
        }

        [Test]
        public void AirZoneFanReserve_StillWidensBounds_AndTheStaticSpan()
        {
            // Rightward zone x ∈ [-1, 1]: the fan box sits behind its left face, at x = -1 - AirZoneFanReserve.
            var zone = new AirZone(new Rect(new Vec2(-1f, 0f), new Vec2(1f, 2f)), new Vec2(1f, 0f), 10f, null);
            var fanX = -1f - CameraFramingMath.AirZoneFanReserve;
            // A post that only the fan reserve brings into the span.
            var post = Filled(fanX + 0.05f, -5f, fanX + 0.1f, -4f);

            var level = Level(Pieces((0f, 1f)), post);
            level.AirZones = new[] { zone };

            Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out _), Is.True);
            Assert.That(min.X, Is.EqualTo(fanX).Within(Eps), "air zone + fan is dynamic content");
            Assert.That(min.Y, Is.EqualTo(-5f).Within(Eps), "the fan-widened span admits the post");
        }

        [Test]
        public void FanlessAirZone_ReservesOnlyItsRegion()
        {
            // TASK-617: the same rightward zone with hasFan:false draws no fan, so the box stops at its face x = -1.
            var zone = new AirZone(new Rect(new Vec2(-1f, 0f), new Vec2(1f, 2f)), new Vec2(1f, 0f), 10f, null,
                hasFan: false);
            var level = Level(Pieces((0f, 1f)));
            level.AirZones = new[] { zone };

            Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out var max), Is.True);
            AssertVec(min, new Vec2(-1f, 0f));
            AssertVec(max, new Vec2(1f, 2f));
        }

        [Test]
        public void NewBounds_SubsetOfOldUnionAabb()
        {
            foreach (var level in Fixtures())
            {
                Assert.That(CameraFramingMath.TryContentBounds(level, out var min, out var max), Is.True);
                OldUnion(level, out var oldMin, out var oldMax);
                Assert.That(min.X, Is.GreaterThanOrEqualTo(oldMin.X - Eps));
                Assert.That(min.Y, Is.GreaterThanOrEqualTo(oldMin.Y - Eps));
                Assert.That(max.X, Is.LessThanOrEqualTo(oldMax.X + Eps));
                Assert.That(max.Y, Is.LessThanOrEqualTo(oldMax.Y + Eps));
            }
        }

        [Test]
        public void PreviewBandCentre_FollowsSafeTop()
        {
            // TASK-576: no notch ⇒ exactly the legacy anchor 1 − band/2.
            Assert.That(CameraFramingMath.PreviewBandCentreViewportY(0.2f, 0f), Is.EqualTo(1f - 0.2f * 0.5f));
            Assert.That(CameraFramingMath.PreviewBandCentreViewportY(0.2f, 0.05f), Is.EqualTo(0.85f).Within(Eps));

            // The card fills at most band·0.85 of the band (TargetPreviewView's pad), so its top edge stays below the
            // safe top on every inset — the card can never slide under a notch / status bar.
            foreach (var safeTop in new[] { 0f, 100f / 2400f, 0.05f, 0.1f })
            {
                var centre = CameraFramingMath.PreviewBandCentreViewportY(0.2f, safeTop);
                var cardTop = centre + 0.2f * 0.85f * 0.5f;
                Assert.That(cardTop, Is.LessThan(1f - safeTop), $"safeTop {safeTop}");
            }
        }

        // ---------------------------------------------------------------- helpers

        private static IEnumerable<LevelData> Fixtures()
        {
            yield return Level(Pieces((-0.75f, 2f), (0.75f, 2f)), Filled(-7f, -0.6f, 7f, 0f));
            yield return Level(Pieces((-0.75f, 2f), (0.75f, 2f)), Filled(3f, 0f, 4f, 20f));
            yield return Level(Pieces((-0.75f, 10f), (0.75f, 10f)), Sliver(new Vec2(-5f, 0f), new Vec2(5f, 5f)));
            yield return Level(Pieces((-0.75f, 2f), (0.75f, 2f)), Surface(new Vec2(-6f, 3f), new Vec2(-4f, 0f)),
                Filled(-7f, -2f, -3f, -1.5f), Filled(3f, 0f, 4f, 20f));
            yield return Level(Pieces((-0.75f, 0f), (0.75f, 0f)),
                new Polygon(new[] { new Vec2(-5f, -3f), new Vec2(-5f, 5f), new Vec2(5f, 5f), new Vec2(5f, -3f) }));
            yield return Level(null, Filled(-7f, -0.6f, 7f, 0f));
        }

        // The pre-TASK-442 box: dynamic content (the function itself with statics removed) ∪ every static vertex.
        private static void OldUnion(LevelData level, out Vec2 min, out Vec2 max)
        {
            var dynamicOnly = new LevelData { Pieces = level.Pieces, Target = level.Target, AirZones = level.AirZones };
            var xs = new List<float>();
            var ys = new List<float>();
            if (CameraFramingMath.TryContentBounds(dynamicOnly, out var dMin, out var dMax))
            {
                xs.Add(dMin.X); xs.Add(dMax.X);
                ys.Add(dMin.Y); ys.Add(dMax.Y);
            }
            foreach (var v in (level.StaticGeometry ?? new Polygon[0]).SelectMany(p => p.Outline))
            {
                xs.Add(v.X);
                ys.Add(v.Y);
            }
            min = new Vec2(xs.Min(), ys.Min());
            max = new Vec2(xs.Max(), ys.Max());
        }

        private static LevelData Level(IReadOnlyList<PiecePlacement> pieces, params Polygon[] statics)
            => new LevelData { Pieces = pieces, StaticGeometry = statics.Length == 0 ? null : statics };

        // 0.5 × 0.5 boxes at the given centres.
        private static IReadOnlyList<PiecePlacement> Pieces(params (float x, float y)[] centres)
        {
            var type = new PieceType("box", Shape.Box(0.5f, 0.5f), Wood);
            return centres.Select(c => new PiecePlacement(type, new Vec2(c.x, c.y))).ToArray();
        }

        private static Polygon Filled(float x0, float y0, float x1, float y1)
            => new Polygon(new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) });

        // A closed two-vertex outline: a segment whose closing edge is itself — isolates edge clipping.
        private static Polygon Sliver(Vec2 a, Vec2 b) => new Polygon(new[] { a, b });

        private static Polygon Surface(params Vec2[] polyline) => new Polygon(polyline, surface: new StaticSurface(0.2f));

        private static void AssertVec(Vec2 actual, Vec2 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Eps));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Eps));
        }
    }
}
