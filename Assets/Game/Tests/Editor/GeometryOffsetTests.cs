using System;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>TASK-613 plan §8 T8: <see cref="Geometry.OffsetOutward"/> — the tour cut-out padding. A square grows to
    /// (s+2d)², either winding goes OUT, a concave L stays finite, and a sharp spike is cut square at miterLimit·d.</summary>
    [TestFixture]
    public sealed class GeometryOffsetTests
    {
        private const float Eps = 1e-4f;

        private static readonly Vec2[] SquareCcw =
        {
            new(0f, 0f), new(2f, 0f), new(2f, 2f), new(0f, 2f),
        };

        [Test]
        public void Square_GrowsByDistanceOnEverySide()
        {
            AssertBounds(Geometry.OffsetOutward(SquareCcw, 0.5f, 2f), -0.5f, -0.5f, 2.5f, 2.5f);
        }

        [Test]
        public void Square_ClockwiseWinding_AlsoGrowsOutward()
        {
            var cw = new Vec2[SquareCcw.Length];
            for (var i = 0; i < cw.Length; i++)
                cw[i] = SquareCcw[cw.Length - 1 - i];

            AssertBounds(Geometry.OffsetOutward(cw, 0.5f, 2f), -0.5f, -0.5f, 2.5f, 2.5f);
        }

        [Test]
        public void LShape_ConcaveCorner_StaysFinite_AndOutsideTheOriginal()
        {
            var l = new Vec2[]
            {
                new(0f, 0f), new(3f, 0f), new(3f, 1f), new(1f, 1f), new(1f, 3f), new(0f, 3f),
            };

            var result = Geometry.OffsetOutward(l, 0.25f, 2f);

            foreach (var p in result)
            {
                Assert.That(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y),
                    Is.False, $"bad vertex {p}");
                Assert.That(Geometry.PointInPolygon(l, p), Is.False, $"{p} landed inside the original L");
            }
            AssertBounds(result, -0.25f, -0.25f, 3.25f, 3.25f);
        }

        [Test]
        public void SharpSpike_IsCutSquare_AtMiterLimitReach()
        {
            // Isosceles triangle with a ~11° apex at (0, 10): a plain miter would reach d / sin(5.7°) ≈ 10·d past it.
            var apex = new Vec2(0f, 10f);
            var tri = new[] { new Vec2(-1f, 0f), new Vec2(1f, 0f), apex };
            const float d = 0.5f;
            const float limit = 2f;

            var result = Geometry.OffsetOutward(tri, d, limit);

            var up = new Vec2(0f, 1f); // the apex bisector, pointing away from the base
            var maxReach = float.MinValue;
            foreach (var p in result)
                maxReach = MathF.Max(maxReach, Vec2.Dot(p - apex, up));
            Assert.That(maxReach, Is.EqualTo(limit * d).Within(Eps), "the spike must be cut at miterLimit·distance");
            Assert.That(result.Length, Is.EqualTo(4), "the cut apex becomes two vertices, the blunt base corners stay one");
        }

        private static void AssertBounds(Vec2[] pts, float minX, float minY, float maxX, float maxY)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts)
            {
                x0 = MathF.Min(x0, p.X);
                y0 = MathF.Min(y0, p.Y);
                x1 = MathF.Max(x1, p.X);
                y1 = MathF.Max(y1, p.Y);
            }
            Assert.That(x0, Is.EqualTo(minX).Within(Eps));
            Assert.That(y0, Is.EqualTo(minY).Within(Eps));
            Assert.That(x1, Is.EqualTo(maxX).Within(Eps));
            Assert.That(y1, Is.EqualTo(maxY).Within(Eps));
        }
    }
}
