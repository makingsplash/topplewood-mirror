using System;
using NUnit.Framework;

namespace SGS.Grid.Tests
{
    public class RectGridMapTests
    {
        [Test]
        public void Constructor_StoresWidthAndHeight()
        {
            var map = new RectGridMap<int>(5, 3);

            Assert.That(map.Width, Is.EqualTo(5));
            Assert.That(map.Height, Is.EqualTo(3));
        }

        [Test]
        public void SetAndGet_RoundTrips()
        {
            var map = new RectGridMap<string>(4, 4);
            map.Set(2, 3, "hello");

            Assert.That(map.Get(2, 3), Is.EqualTo("hello"));
        }

        [Test]
        public void XYIndexer_RoundTrips()
        {
            var map = new RectGridMap<int>(3, 3);
            map[1, 2] = 42;

            Assert.That(map[1, 2], Is.EqualTo(42));
        }

        [Test]
        public void CoordinateIndexer_RoundTrips()
        {
            var map = new RectGridMap<int>(3, 3);
            var coordinate = new RectGridCoordinate(1, 2);
            map[coordinate] = 99;

            Assert.That(map[coordinate], Is.EqualTo(99));
        }

        [Test]
        public void XAndY_AreIndependentDimensions()
        {
            // Verify that X and Y address separate cells — x is the column, y is the row.
            var map = new RectGridMap<int>(4, 3);
            map[2, 0] = 10;
            map[0, 2] = 20;

            Assert.That(map[2, 0], Is.EqualTo(10));
            Assert.That(map[0, 2], Is.EqualTo(20));
            Assert.That(map[2, 2], Is.EqualTo(0));
        }

        [Test]
        public void IsInside_ReturnsTrueForValidCoordinates()
        {
            var map = new RectGridMap<int>(4, 3);

            Assert.That(map.IsInside(0, 0), Is.True);
            Assert.That(map.IsInside(3, 2), Is.True);
            Assert.That(map.IsInside(2, 1), Is.True);
        }

        [Test]
        public void IsInside_ReturnsFalseForOutOfBounds()
        {
            var map = new RectGridMap<int>(4, 3);

            Assert.That(map.IsInside(-1, 0), Is.False);
            Assert.That(map.IsInside(0, -1), Is.False);
            Assert.That(map.IsInside(4, 0), Is.False);
            Assert.That(map.IsInside(0, 3), Is.False);
        }

        [Test]
        public void TryGet_ReturnsFalseForOutOfBounds()
        {
            var map = new RectGridMap<int>(3, 3);

            Assert.That(map.TryGet(5, 0, out _), Is.False);
            Assert.That(map.TryGet(-1, 0, out _), Is.False);
            Assert.That(map.TryGet(0, -1, out _), Is.False);
        }

        [Test]
        public void TryGet_ReturnsTrueAndValueForValidCoordinate()
        {
            var map = new RectGridMap<int>(3, 3);
            map.Set(1, 1, 77);

            Assert.That(map.TryGet(1, 1, out var value), Is.True);
            Assert.That(value, Is.EqualTo(77));
        }

        [Test]
        public void DefaultValue_IsDefaultForType()
        {
            var map = new RectGridMap<int>(2, 2);

            Assert.That(map.Get(0, 0), Is.EqualTo(0));
            Assert.That(map.Get(1, 1), Is.EqualTo(0));
        }

        [Test]
        public void InvalidConstructor_ThrowsOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGridMap<int>(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGridMap<int>(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGridMap<int>(-1, 5));
        }

        [Test]
        public void OutOfBoundsGet_ThrowsOutOfRange()
        {
            var map = new RectGridMap<int>(3, 3);

            Assert.Throws<ArgumentOutOfRangeException>(() => map.Get(5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Get(0, -1));
        }

        [Test]
        public void OutOfBoundsSet_ThrowsOutOfRange()
        {
            var map = new RectGridMap<int>(3, 3);

            Assert.Throws<ArgumentOutOfRangeException>(() => map.Set(0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Set(3, 0, 1));
        }
    }
}
