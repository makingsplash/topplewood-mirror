using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-360 scene-free tests for the FREEFORM primitive: an arbitrary authored silhouette (a closed contour + a
    /// baked convex decomposition, both DATA) that rides the same generic ConvexParts path as the polyominoes. Pure
    /// Unity-free domain code, so it runs in EditMode without a scene. Covers:
    ///  • DTO parse — <c>primitive:"freeform"</c> + <c>outline</c>/<c>parts</c> → Vertices/ConvexParts;
    ///  • validator ACCEPTS an honest concave chevron and REJECTS a self-intersecting contour, a &gt;8-vertex part, and
    ///    a decomposition that does not tile the contour (gap/overlap);
    ///  • the mosaic gate does NOT false-trip on a freeform target slot (§6.3 decoupling) yet STILL engages for a real
    ///    polyomino target (guard that the decoupling did not disable the gate);
    ///  • the symmetryOrder ↔ shape check (§6.4) judges a freeform GEOMETRICALLY — a chiral chevron is order 1, a
    ///    180°-symmetric silhouette admits order 2, and an over-claim is flagged.
    /// The validator is exercised through its PUBLIC entry (<see cref="LevelDataValidator.Validate"/>); each assert
    /// isolates the message substring under test, so the fixture's other (irrelevant) errors do not interfere.
    /// </summary>
    [TestFixture]
    public sealed class FreeformShapeTests
    {
        // ---------------------------------------------------------------- geometry fixtures
        //
        // ChevronContour — an arrow-notch (concave) silhouette, bbox-centred, CCW. The right edge is pushed IN to a V at
        // the origin, so it is chiral (rotational order 1). Its exact convex tiling is the two quads below (each area
        // 1.5, total 3 == the contour area). This is the burger-cheese class the task exists for.
        private static Vec2[] ChevronContour() => new[]
        {
            new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(0f, 0f), new Vec2(1f, 1f), new Vec2(-1f, 1f),
        };

        private static Vec2[][] ChevronParts() => new[]
        {
            new[] { new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(0f, 0f), new Vec2(-1f, 0f) },
            new[] { new Vec2(-1f, 0f), new Vec2(0f, 0f), new Vec2(1f, 1f), new Vec2(-1f, 1f) },
        };

        // SShape — an S-tetromino silhouette authored as a FREEFORM (bbox-centred, CCW). It is symmetric under a 180°
        // rotation about its centroid (order 2) but NOT order 4, so it admits symmetryOrder 2 and flags an over-claim.
        private static Vec2[] SShapeContour() => new[]
        {
            new Vec2(-1.5f, -1f), new Vec2(0.5f, -1f), new Vec2(0.5f, 0f), new Vec2(1.5f, 0f),
            new Vec2(1.5f, 1f), new Vec2(-0.5f, 1f), new Vec2(-0.5f, 0f), new Vec2(-1.5f, 0f),
        };

        private static Vec2[][] SShapeParts() => new[]
        {
            new[] { new Vec2(-1.5f, -1f), new Vec2(0.5f, -1f), new Vec2(0.5f, 0f), new Vec2(-1.5f, 0f) },
            new[] { new Vec2(-0.5f, 0f), new Vec2(1.5f, 0f), new Vec2(1.5f, 1f), new Vec2(-0.5f, 1f) },
        };

        // ---------------------------------------------------------------- DTO parse

        [Test]
        public void Dto_Freeform_ParsesOutlineAndPartsIntoShape()
        {
            var dto = new ShapeDto
            {
                Primitive = "freeform",
                Outline = ToDtoList(ChevronContour()),
                Parts = ChevronParts().Select(ToDtoList).ToList(),
            };

            var shape = dto.ToDomain();

            Assert.That(shape.Primitive, Is.EqualTo(ShapePrimitive.Freeform));
            Assert.That(shape.Radius, Is.EqualTo(0f), "a freeform is straight-edged — no native circle collider");
            Assert.That(shape.Vertices.Length, Is.EqualTo(5), "the contour is the shape's Vertices");
            Assert.That(shape.ConvexParts, Is.Not.Null);
            Assert.That(shape.ConvexParts.Length, Is.EqualTo(2), "the two baked parts become ConvexParts");
            Assert.That(shape.ConvexParts[0].Length, Is.EqualTo(4));
        }

        [Test]
        public void Dto_Freeform_MissingOutline_ThrowsWithNamedCause()
        {
            var dto = new ShapeDto { Primitive = "freeform", Parts = ChevronParts().Select(ToDtoList).ToList() };
            var ex = Assert.Throws<System.InvalidOperationException>(() => dto.ToDomain());
            Assert.That(ex.Message, Does.Contain("outline"));
        }

        [Test]
        public void Dto_Freeform_MissingParts_ThrowsWithNamedCause()
        {
            var dto = new ShapeDto { Primitive = "freeform", Outline = ToDtoList(ChevronContour()) };
            var ex = Assert.Throws<System.InvalidOperationException>(() => dto.ToDomain());
            Assert.That(ex.Message, Does.Contain("parts"));
        }

        // ---------------------------------------------------------------- validator: accept the honest chevron

        [Test]
        public void Validate_HonestChevron_HasNoShapeError()
        {
            var result = ValidatePiece(Freeform("cheese", ChevronContour(), ChevronParts()));
            Assert.That(ShapeErrors(result), Is.Empty,
                "a simple CCW concave contour whose convex parts exactly tile it is legal authoring");
        }

        // ---------------------------------------------------------------- validator: reject malformed authoring

        [Test]
        public void Validate_SelfIntersectingContour_Rejected()
        {
            // A bow-tie contour (edges cross) is not a simple polygon. Parts are irrelevant to this gate.
            var bowtie = new[] { new Vec2(-1f, -1f), new Vec2(1f, 1f), new Vec2(1f, -1f), new Vec2(-1f, 1f) };
            var result = ValidatePiece(Freeform("x", bowtie, ChevronParts()));
            Assert.That(AllMessages(result), Has.Some.Contains("self-intersects"));
        }

        [Test]
        public void Validate_ClockwiseContour_Rejected()
        {
            // The chevron contour reversed is wound clockwise — the validator is the last line for a tool that must
            // emit CCW; it rejects LOUD rather than silently normalising.
            var cw = ChevronContour().Reverse().ToArray();
            var result = ValidatePiece(Freeform("x", cw, ChevronParts()));
            Assert.That(AllMessages(result), Has.Some.Contains("clockwise"));
        }

        [Test]
        public void Validate_PartWithTooManyVertices_Rejected()
        {
            // A convex 9-gon part exceeds Box2D's ≤8-vertex convex cap — the same coordinate-dependent split TASK-072
            // removes. ValidateConvexParts (shared with the polyominoes) catches it.
            var nonagon = new Vec2[9];
            for (var i = 0; i < 9; i++)
            {
                var a = 2f * System.MathF.PI * i / 9f;
                nonagon[i] = new Vec2(System.MathF.Cos(a), System.MathF.Sin(a));
            }
            var result = ValidatePiece(Freeform("x", nonagon, new[] { nonagon }));
            Assert.That(AllMessages(result), Has.Some.Contains("≤8"));
        }

        [Test]
        public void Validate_PartsDoNotTileContour_Rejected()
        {
            // Only ONE of the chevron's two parts — the decomposition covers half the silhouette (a gap). The
            // area-coverage invariant (shared with the polyominoes) flags it.
            var result = ValidatePiece(Freeform("x", ChevronContour(), new[] { ChevronParts()[0] }));
            Assert.That(AllMessages(result), Has.Some.Contains("gaps or overlaps"));
        }

        // ---------------------------------------------------------------- §6.3 mosaic decoupling

        [Test]
        public void Mosaic_FreeformTargetSlot_DoesNotFalseTripTheGate()
        {
            // A freeform target is NOT a grid mosaic (a burger is not решётчатая). The gate must SKIP it, exactly as it
            // skips a circle/triangle slot — not derive a bogus pitch from the freeform's first part and fail.
            var freeform = Freeform("cheese", ChevronContour(), ChevronParts());
            var level = new LevelData
            {
                PhysicsMode = PhysicsMode.Assembly,
                Bounds = new Rect(new Vec2(-100f, -100f), new Vec2(100f, 100f)),
                Pieces = new[]
                {
                    new PiecePlacement(freeform, new Vec2(0f, 0f), preplaced: true),
                    new PiecePlacement(freeform, new Vec2(2f, 0f), preplaced: true),
                },
                Target = new TargetFigure(
                    new Polygon(new[] { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 2f) }),
                    new[]
                    {
                        new TargetSlot("cheese", new Vec2(0f, 0f), 0f),
                        new TargetSlot("cheese", new Vec2(3f, 0f), 0f),
                    }),
            };

            var result = new LevelDataValidator().Validate(level);
            Assert.That(result.Errors, Has.None.Contains("mosaic"),
                "a freeform target slot must not raise a target-mosaic error");
        }

        [Test]
        public void Mosaic_PolyominoTarget_StillEngagesAfterDecoupling()
        {
            // Guard: the §6.3 change (ConvexParts != null → IsGridPolyomino) must NOT disable the gate for a real
            // polyomino. Two identical L-tetromino slots at overlapping poses is a mosaic violation and must still fire.
            var poly = new PieceType("L", Shape.LTetromino(0.5f), new MaterialProfile(1f, 0.5f, 0f));
            var level = new LevelData
            {
                PhysicsMode = PhysicsMode.Assembly,
                Bounds = new Rect(new Vec2(-100f, -100f), new Vec2(100f, 100f)),
                Pieces = new[] { new PiecePlacement(poly, new Vec2(0f, 0f), preplaced: true) },
                Target = new TargetFigure(
                    new Polygon(new[] { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 2f) }),
                    new[]
                    {
                        new TargetSlot("L", new Vec2(0f, 0f), 0f),
                        new TargetSlot("L", new Vec2(0f, 0f), 0f),
                    }),
            };

            var result = new LevelDataValidator().Validate(level);
            Assert.That(result.Errors, Has.Some.Contains("mosaic"),
                "an overlapping polyomino target must still raise a mosaic error (the gate is not disabled)");
        }

        // ---------------------------------------------------------------- §6.4 symmetryOrder (geometric)

        [Test]
        public void Symmetry_ChiralChevron_Order1_IsLegal()
            => Assert.That(SymMessages(ValidateSym(FreeformPiece("chev", ChevronContour(), ChevronParts(), 1))), Is.Empty);

        [Test]
        public void Symmetry_ChiralChevron_Order2_IsFlagged()
        {
            // The chevron is chiral (true order 1) — claiming order 2 would let the pose judge accept a 180°-rotated,
            // physically-different landing. Flagged.
            var msg = SymMessages(ValidateSym(FreeformPiece("chev", ChevronContour(), ChevronParts(), 2)));
            Assert.That(msg, Is.Not.Empty);
        }

        [Test]
        public void Symmetry_C2Silhouette_Order2_IsLegal()
            // The S-silhouette self-coincides at 180° about its centroid — geometric true order 2, so order 2 is legal.
            => Assert.That(SymMessages(ValidateSym(FreeformPiece("s", SShapeContour(), SShapeParts(), 2))), Is.Empty);

        [Test]
        public void Symmetry_C2Silhouette_Order4_IsFlagged()
        {
            // 4 ∤ 2: the S-silhouette has 2-fold symmetry, so order 4 is not a divisor and is flagged.
            var msg = SymMessages(ValidateSym(FreeformPiece("s", SShapeContour(), SShapeParts(), 4)));
            Assert.That(msg, Is.Not.Empty);
        }

        [Test]
        public void Symmetry_FivePointStar_Order5_IsLegal()
            // An ODD order: the 5-point star self-coincides at 72° about its centroid. A candidate list without 5 judged
            // it order 1 and rejected the honest claim (TASK-474, bead_necklace v2).
            => Assert.That(SymMessages(ValidateSym(FreeformPiece("star", StarContour(), StarParts(), 5))), Is.Empty);

        [Test]
        public void Symmetry_FivePointStar_Order10_IsFlagged()
        {
            // 10 ∤ 5: a star is not 10-fold, the over-claim is still caught.
            var msg = SymMessages(ValidateSym(FreeformPiece("star", StarContour(), StarParts(), 10)));
            Assert.That(msg, Is.Not.Empty);
        }

        // StarContour — a regular 5-point star (R 1, r 0.4), CCW from the top tip, then shifted so the BBOX centre is
        // the origin (as authors bake a freeform): the centroid sits off the origin, so the test also pins that the
        // symmetry is judged about the centroid. Parts: the inner pentagon plus one triangle per ray.
        private static Vec2[] StarContour()
        {
            var raw = new Vec2[10];
            for (var i = 0; i < 10; i++)
            {
                var a = System.MathF.PI / 2f + System.MathF.PI * i / 5f;
                var rad = i % 2 == 0 ? 1f : 0.4f;
                raw[i] = new Vec2(rad * System.MathF.Cos(a), rad * System.MathF.Sin(a));
            }
            var minY = raw.Min(v => v.Y);
            var shift = (1f + minY) / 2f;
            return raw.Select(v => new Vec2(v.X, v.Y - shift)).ToArray();
        }

        private static Vec2[][] StarParts()
        {
            var c = StarContour();
            var parts = new Vec2[6][];
            parts[0] = new[] { c[1], c[3], c[5], c[7], c[9] };
            for (var i = 0; i < 5; i++)
                parts[i + 1] = new[] { c[(2 * i + 9) % 10], c[2 * i], c[2 * i + 1] };
            return parts;
        }

        // ---------------------------------------------------------------- helpers

        private static List<Vec2Dto> ToDtoList(Vec2[] verts)
            => verts.Select(v => new Vec2Dto { X = v.X, Y = v.Y }).ToList();

        private static PieceType Freeform(string kind, Vec2[] contour, Vec2[][] parts)
            => new PieceType(kind, Shape.Freeform(contour, parts), new MaterialProfile(1f, 0.5f, 0f));

        private static PieceType FreeformPiece(string kind, Vec2[] contour, Vec2[][] parts, int order)
            => new PieceType(kind, Shape.Freeform(contour, parts), new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: order);

        private static ValidationResult ValidatePiece(PieceType type)
        {
            var placement = new PiecePlacement(type, new Vec2(0f, 0f), preplaced: true);
            return new LevelDataValidator().Validate(new LevelData
            {
                Bounds = new Rect(new Vec2(-100f, -100f), new Vec2(100f, 100f)),
                Pieces = new[] { placement },
            });
        }

        private static ValidationResult ValidateSym(PieceType type)
        {
            var placement = new PiecePlacement(type, new Vec2(0f, 0f), preplaced: true);
            return new LevelDataValidator().Validate(new LevelData { Pieces = new[] { placement } });
        }

        // Freeform contour/part errors all name the offending piece; isolate them from the fixture's other (target/
        // bounds) errors by the shape-specific substrings the checks emit.
        private static string[] ShapeErrors(ValidationResult result)
            => result.Errors.Where(e =>
                e.Contains("freeform contour") || e.Contains("ConvexParts") || e.Contains("gaps or overlaps")).ToArray();

        private static string[] AllMessages(ValidationResult result)
            => result.Errors.Concat(result.Warnings).ToArray();

        private static string[] SymMessages(ValidationResult result)
            => AllMessages(result).Where(m => m.Contains("authors symmetryOrder")).ToArray();
    }
}
