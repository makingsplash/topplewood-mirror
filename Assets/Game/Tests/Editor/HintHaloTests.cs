using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>TASK-641: the facts the hint halo stands on. (1) <see cref="PieceHighlightView"/> pairs the band and
    /// glow rings vertex by vertex, so every <see cref="Geometry.OffsetOutwardRound"/> distance it takes must give the
    /// SAME vertex count — on a sharp 45° triangle (diamond_gem's facet) and a concave L alike; a mismatch makes
    /// <see cref="UiFeatherRing"/> drop the band silently. The round join is a true arc outside the piece. (2) The hinted piece sits BETWEEN the idle and active
    /// looks ("немного более цветной"), not at either end.</summary>
    [TestFixture]
    public sealed class HintHaloTests
    {
        private static readonly Vec2[] SharpTriangle =
        {
            new(0f, 0f), new(2f, 0f), new(0f, 2f), // 90° + two 45° corners
        };

        private const float Step = Mathf.PI / 12f; // PieceHighlightView.Overlay.JoinStepRad

        // A concave L (CW on purpose: winding must not matter): one reflex corner keeps a miter point.
        private static readonly Vec2[] ConcaveLCw =
        {
            new(0f, 0f), new(0f, 2f), new(1f, 2f), new(1f, 1f), new(2f, 1f), new(2f, 0f),
        };

        [Test]
        public void OffsetOutwardRound_SameVertexCount_AtEveryHaloDistance()
        {
            foreach (var outline in new[] { SharpTriangle, ConcaveLCw })
            {
                var counts = new[]
                {
                    Geometry.OffsetOutwardRound(outline, 0.03f, Step).Length,
                    Geometry.OffsetOutwardRound(outline, 0.17f, Step).Length,
                    Geometry.OffsetOutwardRound(outline, 0.37f, Step).Length,
                    Geometry.OffsetOutwardRound(outline, 0.49f, Step).Length,
                };
                Assert.That(counts[0], Is.GreaterThan(outline.Length), "convex corners must become arcs");
                foreach (var c in counts)
                    Assert.That(c, Is.EqualTo(counts[0]));
            }
        }

        [Test]
        public void OffsetOutwardRound_ConvexCornerIsAnArc_AtExactlyTheDistance_AndOutside()
        {
            const float d = 0.3f;
            var ring = Geometry.OffsetOutwardRound(SharpTriangle, d, Step);
            // Every point is at distance ≥ d from the triangle (outside it), and arc points are exactly d from a vertex.
            var onArc = 0;
            foreach (var p in ring)
            {
                var nearestVertex = float.MaxValue;
                foreach (var v in SharpTriangle)
                    nearestVertex = Mathf.Min(nearestVertex, (p - v).Length);
                if (Mathf.Abs(nearestVertex - d) < 1e-4f)
                    onArc++;
                Assert.That(Geometry.PointInPolygon(SharpTriangle, p), Is.False);
            }
            Assert.That(onArc, Is.EqualTo(ring.Length), "a convex triangle's round offset is made of corner arcs only");
        }

        [Test]
        public void HintedTint_IsBetweenIdleAndActive()
        {
            var tuning = new WoodTuning();
            var baseColor = new Color(0.30f, 0.69f, 0.31f); // a green piece — the owner's worst case
            Color.RGBToHSV(tuning.Tint(baseColor, PieceVisualState.Idle), out _, out var sIdle, out _);
            Color.RGBToHSV(tuning.Tint(baseColor, PieceVisualState.Hinted), out _, out var sHint, out _);
            Color.RGBToHSV(tuning.Tint(baseColor, PieceVisualState.Active), out _, out var sActive, out _);

            Assert.That(sHint, Is.GreaterThan(sIdle + 0.05f));
            Assert.That(sHint, Is.LessThan(sActive - 0.05f));
        }
    }
}
