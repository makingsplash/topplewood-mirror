using System.Linq;
using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-445: the target outline stroke (<see cref="SilhouetteView.BuildStrokeMesh"/>). The defect it guards:
    /// <c>LineRenderer</c> shifted each ribbon vertex along the join bisector, so next to a short edge and a sharp
    /// corner (roof overhang 0.2 over a post, base step 0.1) a vertical wall came out slanted — the left wall of
    /// <c>kennel_ball</c> drew at x −0.56 top / −0.49 bottom against data −0.5 / −0.5. The contract here: every
    /// data edge is a rectangle lying EXACTLY on that edge at ±halfWidth, built from the edge alone.
    /// </summary>
    [TestFixture]
    public sealed class SilhouetteStrokeTests
    {
        private const float HalfWidth = 0.04f;
        private const float Eps = 1e-5f;

        // kennel_ball target.outline (manifest of TASK-438): overhang (−0.7,1.6)→(−0.5,1.6) sits at a sharp
        // corner to the roof slope right above the vertical left wall (−0.5,1.6)→(−0.5,0.4), and a 0.1 base step
        // follows it.
        private static readonly Vec2[] Kennel =
        {
            new Vec2(-0.6f, 0f), new Vec2(1.4f, 0f), new Vec2(1.4f, 0.4f), new Vec2(1.2f, 0.4f),
            new Vec2(1.2f, 1.6f), new Vec2(1.4f, 1.6f), new Vec2(0.35f, 2.6f), new Vec2(-0.7f, 1.6f),
            new Vec2(-0.5f, 1.6f), new Vec2(-0.5f, 0.4f), new Vec2(-0.6f, 0.4f),
        };

        private const int KennelLeftWall = 8;

        [Test]
        public void Kennel_LeftWall_IsVerticalAtDataX()
        {
            var mesh = SilhouetteView.BuildStrokeMesh(Kennel, HalfWidth, 0.1f);
            try
            {
                var rect = EdgeRect(mesh.vertices, KennelLeftWall);
                var xs = rect.Select(v => v.x).Distinct(new Close()).OrderBy(x => x).ToArray();
                Assert.That(xs.Length, Is.EqualTo(2), "a vertical wall has exactly two x values on its rectangle");
                Assert.That(xs[0], Is.EqualTo(-0.5f - HalfWidth).Within(Eps));
                Assert.That(xs[1], Is.EqualTo(-0.5f + HalfWidth).Within(Eps));
                Assert.That(rect.Min(v => v.y), Is.EqualTo(0.4f).Within(Eps));
                Assert.That(rect.Max(v => v.y), Is.EqualTo(1.6f).Within(Eps));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Kennel_EveryEdgeRect_LiesOnItsEdge_IndependentOfNeighbours()
        {
            var mesh = SilhouetteView.BuildStrokeMesh(Kennel, HalfWidth, 0.1f);
            try
            {
                var vertices = mesh.vertices;
                for (var i = 0; i < Kennel.Length; i++)
                {
                    var a = Kennel[i];
                    var b = Kennel[(i + 1) % Kennel.Length];
                    var expected = ExpectedRect(a, b);
                    var actual = EdgeRect(vertices, i);
                    for (var k = 0; k < 4; k++)
                    {
                        Assert.That(actual[k].x, Is.EqualTo(expected[k].x).Within(Eps), $"edge {i} corner {k} x");
                        Assert.That(actual[k].y, Is.EqualTo(expected[k].y).Within(Eps), $"edge {i} corner {k} y");
                    }

                    // Same edge with no neighbours at all must yield the same rectangle.
                    var lone = SilhouetteView.BuildStrokeMesh(new[] { a, b }, HalfWidth, 0.1f);
                    try
                    {
                        var loneRect = EdgeRect(lone.vertices, 0);
                        for (var k = 0; k < 4; k++)
                            Assert.That(Vector3.Distance(actual[k], loneRect[k]), Is.LessThan(Eps),
                                $"edge {i} corner {k} depends on its neighbours");
                    }
                    finally { Object.DestroyImmediate(lone); }
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void DegenerateEdge_IsSkipped_WithoutNaN()
        {
            // Unit square with a duplicated corner: edge (1,0)→(1,0) is zero-length.
            var outline = new[]
            {
                new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f), new Vec2(0f, 1f),
            };
            var mesh = SilhouetteView.BuildStrokeMesh(outline, HalfWidth, 0f);
            try
            {
                var vertices = mesh.vertices;
                Assert.That(vertices.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)),
                    "no NaN vertices");
                const int realEdges = 4;
                Assert.That(vertices.Length,
                    Is.EqualTo(realEdges * 4 + outline.Length * (1 + SilhouetteView.JoinSegments)),
                    "the degenerate edge contributes no rectangle");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TriangleCount_IsRectPerEdgePlusCirclePerVertex_TwoSided()
        {
            var mesh = SilhouetteView.BuildStrokeMesh(Kennel, HalfWidth, 0.1f);
            try
            {
                var n = Kennel.Length;
                var oneSided = n * 2 + n * SilhouetteView.JoinSegments;
                Assert.That(mesh.triangles.Length, Is.EqualTo(oneSided * 2 * 3),
                    "2 triangles per edge rectangle + JoinSegments per vertex circle, each emitted both windings");
                Assert.That(mesh.vertexCount, Is.EqualTo(n * 4 + n * (1 + SilhouetteView.JoinSegments)));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static Vector3[] EdgeRect(Vector3[] vertices, int edge) =>
            new[] { vertices[edge * 4], vertices[edge * 4 + 1], vertices[edge * 4 + 2], vertices[edge * 4 + 3] };

        // a−n, b−n, b+n, a+n with n the left normal of a→b scaled to HalfWidth — computed from the edge alone.
        private static Vector2[] ExpectedRect(Vec2 a, Vec2 b)
        {
            var d = new Vector2(b.X - a.X, b.Y - a.Y);
            var n = new Vector2(-d.y, d.x).normalized * HalfWidth;
            var va = new Vector2(a.X, a.Y);
            var vb = new Vector2(b.X, b.Y);
            return new[] { va - n, vb - n, vb + n, va + n };
        }

        private sealed class Close : System.Collections.Generic.IEqualityComparer<float>
        {
            public bool Equals(float x, float y) => Mathf.Abs(x - y) < Eps;
            public int GetHashCode(float value) => 0;
        }
    }
}
