using System.Linq;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-628: <see cref="LevelDataValidator"/> warns when a PREPLACED piece starts above its support (it visibly drops
    /// at level start — the owner's potted_tree playtest). Pins the measure on the two supports a preplaced piece can
    /// have (static block, another preplaced piece), the flush case, the "nothing below" case, and that a tapable piece
    /// is never a support (it hangs outside physics until tapped). Scene-free.
    /// </summary>
    public sealed class PreplacedRestTests
    {
        private static readonly Polygon Floor = new(new[]
        {
            new Vec2(-3f, -1f), new Vec2(3f, -1f), new Vec2(3f, 0f), new Vec2(-3f, 0f),
        });

        [Test]
        public void BoxHoveringOverStatic_WarnsWithGap()
        {
            // potted_tree's stick: bottom 0.10 above the block top.
            var level = Level(new PiecePlacement(Box(1f, 0.4f), new Vec2(0f, 0.3f), preplaced: true));
            Assert.That(LevelDataValidator.PreplacedDrop(level, 0, out var support), Is.EqualTo(0.1f).Within(0.002f));
            Assert.That(support, Is.EqualTo("StaticGeometry[0]"));
            Assert.That(RestWarnings(level), Has.Length.EqualTo(1));
        }

        [Test]
        public void BoxRestingFlushOnStatic_NoWarning()
        {
            var level = Level(new PiecePlacement(Box(1f, 0.4f), new Vec2(0f, 0.2f), preplaced: true));
            Assert.That(LevelDataValidator.PreplacedDrop(level, 0, out _), Is.LessThanOrEqualTo(0.002f));
            Assert.That(RestWarnings(level), Is.Empty);
        }

        [Test]
        public void CircleHoveringOverPreplacedPiece_NamesThatPiece()
        {
            // potted_tree's crown: 0.05 above the (resting) stick.
            var level = Level(
                new PiecePlacement(Box(2f, 0.4f), new Vec2(0f, 0.2f), preplaced: true),
                new PiecePlacement(Circle(0.5f), new Vec2(0.5f, 0.95f), preplaced: true));
            Assert.That(LevelDataValidator.PreplacedDrop(level, 1, out var support), Is.EqualTo(0.05f).Within(0.002f));
            Assert.That(support, Is.EqualTo("Pieces[0] 'box'"));
            Assert.That(RestWarnings(level), Has.Length.EqualTo(1));
        }

        [Test]
        public void TapablePieceIsNotASupport()
        {
            // The box below is tapable (Idle, outside physics): the circle falls past it onto the floor.
            var level = Level(
                new PiecePlacement(Box(2f, 0.4f), new Vec2(0f, 0.2f)),
                new PiecePlacement(Circle(0.5f), new Vec2(0.5f, 0.9f), preplaced: true));
            Assert.That(LevelDataValidator.PreplacedDrop(level, 1, out var support), Is.EqualTo(0.4f).Within(0.002f));
            Assert.That(support, Is.EqualTo("StaticGeometry[0]"));
        }

        [Test]
        public void NothingBelow_NotReported()
        {
            var level = Level(new PiecePlacement(Box(1f, 0.4f), new Vec2(5f, 3f), preplaced: true));
            Assert.That(LevelDataValidator.PreplacedDrop(level, 0, out _), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RestWarnings(level), Is.Empty);
        }

        private static LevelData Level(params PiecePlacement[] pieces) => new()
        {
            StaticGeometry = new[] { Floor },
            Pieces = pieces,
            Bounds = new Rect(new Vec2(-6f, -6f), new Vec2(6f, 10f)),
        };

        private static PieceType Box(float w, float h)
            => new("box", Shape.Box(w, h), new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: 2);

        private static PieceType Circle(float radius)
            => new("ball", Shape.Circle(radius, 24), new MaterialProfile(1f, 0.5f, 0f), symmetryOrder: 24);

        private static string[] RestWarnings(LevelData level)
            => new LevelDataValidator().Validate(level).Warnings.Where(w => w.StartsWith("Preplaced rest:")).ToArray();
    }
}
