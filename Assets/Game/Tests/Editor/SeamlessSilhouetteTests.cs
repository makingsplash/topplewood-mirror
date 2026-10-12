using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-368: a compound FREEFORM piece is drawn without internal seams — the view measures the edge against the
    /// piece's SILHOUETTE (shader variant <c>_CONCAVE_SDF</c>), not against the per-part fan whose internal edges carry
    /// distance 0. These tests pin the scope predicate (freeform only), the uniform packing (Unity fixes an MPB array's
    /// length on first set) and the live re-colour path (Clear() wipes the outline).
    /// </summary>
    public sealed class SeamlessSilhouetteTests
    {
        // Same chevron as FreeformShapeTests: CCW contour, two convex parts sharing the chord (-1,0)→(0,0). Both chord
        // endpoints lie ON the silhouette — (0,0) is a contour vertex, (-1,0) sits on the left edge — which is exactly why
        // a per-vertex distance cannot hide the chord.
        private static Vec2[] ChevronContour() => new[]
        {
            new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(0f, 0f), new Vec2(1f, 1f), new Vec2(-1f, 1f),
        };

        private static Vec2[][] ChevronParts() => new[]
        {
            new[] { new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(0f, 0f), new Vec2(-1f, 0f) },
            new[] { new Vec2(-1f, 0f), new Vec2(0f, 0f), new Vec2(1f, 1f), new Vec2(-1f, 1f) },
        };

        private static Shape Chevron() => Shape.Freeform(ChevronContour(), ChevronParts());

        private static Vec2[] RegularPolygon(int n)
        {
            var v = new Vec2[n];
            for (var i = 0; i < n; i++)
            {
                var a = 2f * Mathf.PI * i / n;
                v[i] = new Vec2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return v;
        }

        [Test]
        public void Freeform_SeamlessSilhouette_IsTheContour()
        {
            var shape = Chevron();
            var sil = PieceMeshBuilder.SeamlessSilhouette(shape);
            Assert.That(sil, Is.SameAs(shape.Vertices), "the shader must measure against the authored silhouette");
            Assert.That(sil.Length, Is.EqualTo(5));
        }

        [Test]
        public void Polyomino_SeamlessSilhouette_IsNull()
        {
            // Pins the scope: polyominoes keep the tile seam (27 shipped levels). Flipping this is a deliberate decision.
            Assert.That(PieceMeshBuilder.SeamlessSilhouette(Shape.LTetromino(1f)), Is.Null);
        }

        [Test]
        public void ConvexPrimitive_SeamlessSilhouette_IsNull()
        {
            Assert.That(PieceMeshBuilder.SeamlessSilhouette(Shape.Box(1f, 2f)), Is.Null);
        }

        [Test]
        public void OverLimit_FallsBackToNull()
        {
            var n = BlockMaterial.SilhouetteMax + 1;
            var poly = RegularPolygon(n);
            var shape = Shape.Freeform(poly, new[] { poly });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("shader-uniform limit"));
            Assert.That(PieceMeshBuilder.SeamlessSilhouette(shape), Is.Null,
                "a contour the uniform cannot hold falls back to the seam path instead of a truncated outline");
            Assert.That(BlockMaterial.PackSilhouette(poly), Is.Null, "packing refuses rather than truncates");
        }

        [Test]
        public void ChordMidpoint_IsInterior_UnderSilhouetteSdf()
        {
            // C# mirror of SilhouetteSdf.hlsl: the chord midpoint is ~0.5 inside the silhouette…
            var mid = new Vec2(-0.5f, 0f);
            var d = Geometry.NearestBoundaryEdge(ChevronContour(), mid, out _);
            Assert.That(d, Is.GreaterThanOrEqualTo(0.45f));

            // …while under the per-part fan it lies ON a part edge (the shared chord), where the mesh writes uv2.x = 0 —
            // the recorded cause of the seam.
            var dPart = Geometry.NearestBoundaryEdge(ChevronParts()[0], mid, out _);
            Assert.That(dPart, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void PackSilhouette_AlwaysMaxLength_TailZero()
        {
            var packed = BlockMaterial.PackSilhouette(ChevronContour());
            Assert.That(packed.Length, Is.EqualTo(BlockMaterial.SilhouetteMax));
            Assert.That(packed[4], Is.EqualTo(new Vector4(-1f, 1f, 0f, 0f)));
            Assert.That(packed[5], Is.EqualTo(Vector4.zero));
            Assert.That(packed[BlockMaterial.SilhouetteMax - 1], Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void SetSilhouette_LongerAfterShorter_NotTruncated()
        {
            // One MPB, short outline first, longer second, NO Clear() in between. Unity fixes the array length on the FIRST
            // SetVectorArray and silently truncates a longer one later; without full-length packing the 40-vertex outline
            // would lose its tail. (Measured: after mpb.Clear() the length is NOT kept — the trap lives only on a block
            // reused without Clear, which is how TargetPreviewView.Repaint worked before TASK-368.)
            var mpb = new MaterialPropertyBlock();
            BlockMaterial.SetSilhouette(mpb, BlockMaterial.PackSilhouette(ChevronContour()), 5);

            var forty = RegularPolygon(40);
            BlockMaterial.SetSilhouette(mpb, BlockMaterial.PackSilhouette(forty), forty.Length);

            var got = mpb.GetVectorArray(BlockMaterial.ConcaveOutlineProperty);
            Assert.That(got.Length, Is.GreaterThanOrEqualTo(forty.Length), "the outline tail was truncated");
            Assert.That(got[39].x, Is.EqualTo(forty[39].X).Within(1e-6f));
            Assert.That(got[39].y, Is.EqualTo(forty[39].Y).Within(1e-6f));
            Assert.That(mpb.GetFloat(BlockMaterial.ConcaveOutlineCountProperty), Is.EqualTo(40f));
        }

        [Test]
        public void PieceView_Freeform_UsesSdfMaterial_AndSurvivesRecolor()
        {
            var go = new GameObject("SeamlessSilhouetteTests_freeform");
            try
            {
                var view = go.AddComponent<PieceView>();
                view.Initialize(Chevron(), "#C04040");
                var renderer = go.GetComponent<MeshRenderer>();

                Assert.That(renderer.sharedMaterial, Is.SameAs(BlockMaterial.ConcaveShared));
                Assert.That(renderer.sharedMaterial.IsKeywordEnabled(BlockMaterial.ConcaveKeyword), Is.True);
                Assert.That(OutlineCount(renderer), Is.EqualTo(5f));

                // Re-colour goes through mpb.Clear(); the outline must be re-uploaded, not lost.
                view.SetState(PieceLifecycle.Falling);
                Assert.That(OutlineCount(renderer), Is.EqualTo(5f));

                // The shadow measures the same silhouette (else the chord cuts a transparent crack in it).
                var shadow = go.transform.Find("Shadow");
                Assert.That(shadow, Is.Not.Null);
                var shadowRenderer = shadow.GetComponent<MeshRenderer>();
                Assert.That(shadowRenderer.sharedMaterial, Is.SameAs(PieceShadow.SilhouetteShared));
                Assert.That(OutlineCount(shadowRenderer), Is.EqualTo(5f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PieceView_Polyomino_KeepsSharedMaterial()
        {
            var go = new GameObject("SeamlessSilhouetteTests_polyomino");
            try
            {
                go.AddComponent<PieceView>().Initialize(Shape.LTetromino(1f), "#40C040");
                var renderer = go.GetComponent<MeshRenderer>();
                Assert.That(renderer.sharedMaterial, Is.SameAs(BlockMaterial.Shared));
                Assert.That(go.transform.Find("Shadow").GetComponent<MeshRenderer>().sharedMaterial,
                    Is.SameAs(PieceShadow.Shared));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static float OutlineCount(Renderer renderer)
        {
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            return mpb.GetFloat(BlockMaterial.ConcaveOutlineCountProperty);
        }
    }
}
