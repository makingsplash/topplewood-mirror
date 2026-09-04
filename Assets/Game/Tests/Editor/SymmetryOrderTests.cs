using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-340 scene-free tests for the symmetryOrder ↔ shape validator check (<see cref="LevelDataValidator"/> #13):
    /// a piece's authored <c>symmetryOrder</c> must DIVIDE the shape's true rotational-symmetry order, or the pose
    /// judge silently accepts a rotated (physically-different) landing as a match. Pure Unity-free domain code, so it
    /// runs in EditMode without a scene. The validator is exercised through its PUBLIC entry
    /// (<see cref="LevelDataValidator.Validate"/>): a fixture sets only <c>Pieces</c>, so the other checks add their own
    /// (irrelevant) errors while we assert on the symmetry message alone (it is the only one carrying "authors
    /// symmetryOrder"). Routing (WARNING in phase 1, ERROR in phase 2) is asserted against the shipped
    /// <see cref="LevelDataValidator.SymmetryOrderMismatchIsError"/> flag, so the SAME tests still pass after the
    /// phase-2 flip.
    /// </summary>
    [TestFixture]
    public sealed class SymmetryOrderTests
    {
        // ---------------------------------------------------------------- illegal (flagged with a named cause)

        [Test]
        public void NonSquareBox_Order4_IsFlaggedWithNamedCause()
        {
            var msg = SymMessages(ValidateSym(Piece("body", Shape.Box(1.0f, 0.7f), 4))).Single();
            Assert.That(msg, Does.Contain("symmetryOrder 4"), "the authored order is named");
            Assert.That(msg, Does.Contain("2-fold"), "a non-square box has 2-fold symmetry — the cause is named");
            Assert.That(msg, Does.Contain("box 1.000×0.700"), "the offending dims are named");
        }

        [Test]
        public void NonSquareBox_Order4_RoutesByThePhaseFlag()
        {
            var result = ValidateSym(Piece("body", Shape.Box(1.0f, 0.7f), 4));
            // Phase 1 ships false ⇒ the mismatch is a non-blocking WARNING and the level still loads (Ok stays true for
            // this reason). The assertion tracks the flag so it still holds after the phase-2 flip to ERROR.
            if (LevelDataValidator.SymmetryOrderMismatchIsError)
                Assert.That(result.Errors, Has.Some.Contains("authors symmetryOrder"));
            else
                Assert.That(result.Warnings, Has.Some.Contains("authors symmetryOrder"));
        }

        [Test]
        public void NonSquarePlank_Order4_IsFlagged()
            => Assert.That(SymMessages(ValidateSym(Piece("mast", Shape.Plank(0.5f, 2.6f), 4))), Is.Not.Empty);

        [Test]
        public void Square_Order3_IsFlagged_DivisibilityNotLessEqual()
        {
            // 3 ∤ 4: a square has 4-fold symmetry but order 3 is NOT a divisor, so the 120° fold is not a real symmetry.
            var msg = SymMessages(ValidateSym(Piece("tile", Shape.Box(0.6f, 0.6f), 3))).Single();
            Assert.That(msg, Does.Contain("symmetryOrder 3"));
            Assert.That(msg, Does.Contain("4-fold"));
        }

        [Test]
        public void Triangle_Order2_IsFlagged()
            => Assert.That(SymMessages(ValidateSym(Piece("roof", Shape.Triangle(1f, 1f), 2))), Is.Not.Empty);

        [Test]
        public void Parallelogram_Order4_IsFlagged()
            => Assert.That(SymMessages(ValidateSym(Piece("slab", Shape.Parallelogram(1f, 0.6f, 0.3f), 4))), Is.Not.Empty);

        [Test]
        public void ToleranceBoundary_270x275_Order4_IsFlagged()
        {
            // TASK-339's box: 2.70×2.75 is 1.8% off square — well past the 1e-3 SquareTolerance, so it must FAIL.
            // Guards against a loosened ε silently reintroducing the original bug.
            Assert.That(SymMessages(ValidateSym(Piece("near", Shape.Box(2.70f, 2.75f), 4))), Is.Not.Empty);
        }

        // ---------------------------------------------------------------- legal (silent — no symmetry message)

        [Test]
        public void SquareBox_Order4_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("tile", Shape.Box(0.6f, 0.6f), 4))), Is.Empty);

        [Test]
        public void Rectangle_Order2_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("bar", Shape.Box(1.0f, 0.7f), 2))), Is.Empty);

        [Test]
        public void Rectangle_Order1_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("bar", Shape.Box(1.0f, 0.7f), 1))), Is.Empty);

        [Test]
        public void Square_Order1_IsLegal_ProperDivisorIsStricterNotWrong()
            // A proper divisor (1 | 4) is merely a stricter judge — it can only reject, never falsely accept — so legal.
            => Assert.That(SymMessages(ValidateSym(Piece("tile", Shape.Box(0.6f, 0.6f), 1))), Is.Empty);

        [Test]
        public void SquarePlank_Order4_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("cube", Shape.Plank(0.7f, 0.7f), 4))), Is.Empty);

        [Test]
        public void STetromino_Order2_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("s", Shape.STetromino(0.5f), 2))), Is.Empty);

        [Test]
        public void ZTetromino_Order2_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("z", Shape.ZTetromino(0.5f), 2))), Is.Empty);

        [Test]
        public void PlusPentomino_Order4_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("plus", Shape.PlusPentomino(0.5f), 4))), Is.Empty);

        [Test]
        public void ZPentomino_Order2_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("z5", Shape.ZPentomino(0.5f), 2))), Is.Empty);

        [Test]
        public void Circle_HighOrder_IsExempt()
            // A round piece has no meaningful angle; the corpus authors a high order deliberately. Any order is legal.
            => Assert.That(SymMessages(ValidateSym(Piece("ball", Shape.Circle(1f, 24), 24))), Is.Empty);

        [Test]
        public void Circle_Order3_IsExempt_EvenWhenNonDividing()
            // Exempt is unconditional: 3 ∤ 24 would be flagged for a straight-edged shape, but a circle is skipped.
            => Assert.That(SymMessages(ValidateSym(Piece("ball", Shape.Circle(1f, 24), 3))), Is.Empty);

        [Test]
        public void Triangle_Order1_IsLegal()
            => Assert.That(SymMessages(ValidateSym(Piece("roof", Shape.Triangle(1f, 1f), 1))), Is.Empty);

        // ---------------------------------------------------------------- helpers

        private static PieceType Piece(string kind, Shape shape, int order)
            => new PieceType(kind, shape, new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: order);

        private static ValidationResult ValidateSym(params PieceType[] types)
        {
            var placements = new PiecePlacement[types.Length];
            for (var i = 0; i < types.Length; i++)
                placements[i] = new PiecePlacement(types[i], new Vec2(0f, 0f), preplaced: true);
            return new LevelDataValidator().Validate(new LevelData { Pieces = placements });
        }

        // The symmetry check is the only one whose message carries "authors symmetryOrder"; the fixture's other
        // (irrelevant) errors — null target, out-of-bounds start — never do, so this isolates the check under test
        // across both the Errors (phase 2) and Warnings (phase 1) channels.
        private static string[] SymMessages(ValidationResult result)
        {
            var all = new List<string>(result.Errors);
            all.AddRange(result.Warnings);
            return all.Where(m => m.Contains("authors symmetryOrder")).ToArray();
        }
    }
}
