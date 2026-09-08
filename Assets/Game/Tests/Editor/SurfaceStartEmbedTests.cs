using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-329: <see cref="LevelDataValidator"/> measures a piece's START embedding in a SURFACE against the
    /// ZERO-WIDTH edge-chain centreline (the <c>EdgeCollider2D</c> the sim builds from the outline), not the
    /// ±½·thickness rendered ribbon. These scene-free tests pin the new semantics: a piece resting FLUSH on the
    /// centreline reads 0 (the old ribbon measure over-reported it by half the thickness — the false "starts
    /// embedded"), a piece the chain runs THROUGH is flagged, flat-on-flat resting is NOT false-flagged, and the
    /// FILLED-solid path is unchanged. The fixture wires only StaticGeometry + Pieces, so the embed messages
    /// ("starts embedded") are isolated from the other (irrelevant) validator errors.
    /// </summary>
    public sealed class SurfaceStartEmbedTests
    {
        // ---------------------------------------------------------------- surface: circle (chute_cross's red_ball)

        [Test]
        public void CircleRestingFlushOnSurface_IsNotEmbedded()
        {
            // The core TASK-329 regress: a ball whose bottom touches a horizontal chain (centre exactly one radius
            // above) rests FLUSH. The old ribbon measure read ½·thickness (0.1) here and false-flagged; the centreline
            // measure reads 0.
            var chain = Surface(0.2f, new Vec2(-3f, 0f), new Vec2(3f, 0f));
            Assert.That(EmbedErrors(Validate(chain, Circle(0.75f), new Vec2(0f, 0.75f))), Is.Empty);
        }

        [Test]
        public void CircleThreadedOnSurface_IsEmbedded()
        {
            // The chain runs through the ball's centre — a real embed (the piece is impaled on the film).
            var chain = Surface(0.2f, new Vec2(-3f, 0f), new Vec2(3f, 0f));
            Assert.That(EmbedErrors(Validate(chain, Circle(0.75f), new Vec2(0f, 0f))), Is.Not.Empty);
        }

        [Test]
        public void CircleSunkBelowFlush_IsEmbedded()
        {
            // Centre 0.05 below the flush height ⇒ the chain sinks 0.05 into the disc — above MinStaticPenetration.
            var chain = Surface(0.2f, new Vec2(-3f, 0f), new Vec2(3f, 0f));
            Assert.That(EmbedErrors(Validate(chain, Circle(0.75f), new Vec2(0f, 0.70f))), Is.Not.Empty);
        }

        [Test]
        public void CircleNestledInConcaveThroat_IsNotEmbedded()
        {
            // A V-throat (concave chute): a disc tangent to the wall rests flush. On a concave surface a convex-hull
            // measure would lie — the per-segment centreline measure does not. Wall (-2,2)->(0,0) has unit normal
            // (1,1)/√2; a disc of r=0.5 tangent to it and centred on x=0 sits at (0, 0.5·√2) ≈ (0, 0.7071).
            var chain = Surface(0.2f, new Vec2(-2f, 2f), new Vec2(0f, 0f), new Vec2(2f, 2f));
            Assert.That(EmbedErrors(Validate(chain, Circle(0.5f), new Vec2(0f, 0.7071f))), Is.Empty);
        }

        // ---------------------------------------------------------------- surface: polygon pieces (pierce depth)

        [Test]
        public void BoxRestingFlatOnSurface_IsNotEmbedded()
        {
            // A box sitting flat on a flat chain: its bottom edge lies ON the centreline. This is the case a naive CHORD
            // measure breaks on (the collinear overlap reads as a long chord); the pierce-depth measure reads 0 because
            // the segment never enters the interior.
            var chain = Surface(0.2f, new Vec2(-3f, 0f), new Vec2(3f, 0f));
            Assert.That(EmbedErrors(Validate(chain, Box(1f, 0.6f), new Vec2(0f, 0.3f))), Is.Empty);
        }

        [Test]
        public void BoxImpaledBySurface_IsEmbedded()
        {
            // The chain runs through the box's middle (centre on the chain): the deepest interior point is half the
            // box height (0.3) from the boundary — a real embed.
            var chain = Surface(0.2f, new Vec2(-3f, 0f), new Vec2(3f, 0f));
            Assert.That(EmbedErrors(Validate(chain, Box(1f, 0.6f), new Vec2(0f, 0f))), Is.Not.Empty);
        }

        // ---------------------------------------------------------------- surface: collider:false is skipped

        [Test]
        public void VisualOnlySurface_IsNeverAnEmbedSource()
        {
            // collider:false ⇒ VISUAL-ONLY (TASK-346): even a piece sitting on the centreline is not embedded in it.
            var chain = new Polygon(new[] { new Vec2(-3f, 0f), new Vec2(3f, 0f) }, null, new StaticSurface(0.2f), false);
            Assert.That(EmbedErrors(Validate(chain, Circle(0.75f), new Vec2(0f, 0f))), Is.Empty);
        }

        // ---------------------------------------------------------------- filled solid path is UNCHANGED

        [Test]
        public void FilledSolid_PieceInside_StillEmbedded()
        {
            // The filled-solid branch is untouched by TASK-329: a piece buried in a filled block is still flagged.
            var block = Filled(new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 2f), new Vec2(0f, 2f));
            Assert.That(EmbedErrors(Validate(block, Circle(0.5f), new Vec2(1f, 1f))), Is.Not.Empty);
        }

        [Test]
        public void FilledSolid_PieceRestingOnTop_NotEmbedded()
        {
            var block = Filled(new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 2f), new Vec2(0f, 2f));
            Assert.That(EmbedErrors(Validate(block, Circle(0.5f), new Vec2(1f, 2.5f))), Is.Empty);
        }

        // ---------------------------------------------------------------- helpers

        private static Polygon Surface(float thickness, params Vec2[] outline)
            => new Polygon(outline, null, new StaticSurface(thickness));

        private static Polygon Filled(params Vec2[] outline) => new Polygon(outline);

        private static PieceType Circle(float radius)
            => new PieceType("ball", Shape.Circle(radius, 24), new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: 24);

        private static PieceType Box(float w, float h)
            => new PieceType("box", Shape.Box(w, h), new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: 2);

        private static ValidationResult Validate(Polygon block, PieceType type, Vec2 startPos)
        {
            var level = new LevelData
            {
                StaticGeometry = new[] { block },
                Pieces = new[] { new PiecePlacement(type, startPos, preplaced: true) },
            };
            return new LevelDataValidator().Validate(level);
        }

        private static string[] EmbedErrors(ValidationResult result)
            => result.Errors.Where(e => e.Contains("starts embedded")).ToArray();
    }
}
