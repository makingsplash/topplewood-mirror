#if GPIE_DEVELOPMENT
using Gameplay.Dev;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-587: the DEV chip's position is stored as a fraction of the free range, so a spot chosen on one screen
    /// means the same place on another, and a drag can never leave the safe area.
    /// Mutations these catch: fraction stored in absolute units (second test: 0.75·880 = 660 clamps to 1 → 880, not 660),
    /// clamp missing an edge (third), a zone larger than the area going negative (fourth).
    /// </summary>
    [TestFixture]
    public sealed class DevChipDragTests
    {
        [Test]
        public void FractionRoundTripsOnTheSameScreen()
        {
            var free = DevChipDrag.FreeRange(new Vector2(1080f, 1920f), new Vector2(200f, 116f));
            var position = new Vector2(300f, 700f);

            var back = DevChipDrag.FromFraction(DevChipDrag.ToFraction(position, free), free);

            Assert.That(back.x, Is.EqualTo(position.x).Within(1e-3f));
            Assert.That(back.y, Is.EqualTo(position.y).Within(1e-3f));
        }

        // A mid-range spot, not a corner: at a corner even an absolute-units store clamps to 1 and passes.
        [Test]
        public void SpotKeepsItsShareOfTheScreenOnATallerScreen()
        {
            var zone = new Vector2(200f, 116f);
            var shortFree = DevChipDrag.FreeRange(new Vector2(1080f, 1920f), zone);
            var tallFree = DevChipDrag.FreeRange(new Vector2(1080f, 2340f), zone);

            var fraction = DevChipDrag.ToFraction(new Vector2(0.75f * shortFree.x, 0.25f * shortFree.y), shortFree);
            var onTall = DevChipDrag.FromFraction(fraction, tallFree);

            Assert.That(onTall.x, Is.EqualTo(0.75f * tallFree.x).Within(1e-3f));
            Assert.That(onTall.y, Is.EqualTo(0.25f * tallFree.y).Within(1e-3f));
        }

        [Test]
        public void ClampKeepsTheZoneInsideOnEveryEdge()
        {
            var free = new Vector2(880f, 1804f);

            Assert.That(DevChipDrag.Clamp(new Vector2(-50f, -50f), free), Is.EqualTo(Vector2.zero));
            Assert.That(DevChipDrag.Clamp(new Vector2(5000f, 5000f), free), Is.EqualTo(free));
        }

        [Test]
        public void ZoneLargerThanAreaHasNoFreeRange()
        {
            var free = DevChipDrag.FreeRange(new Vector2(100f, 100f), new Vector2(200f, 116f));

            Assert.That(free, Is.EqualTo(Vector2.zero));
            Assert.That(DevChipDrag.ToFraction(new Vector2(10f, 10f), free), Is.EqualTo(Vector2.zero));
        }
    }
}
#endif
