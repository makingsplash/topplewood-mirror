using System;
using NUnit.Framework;
using UnityEngine;

namespace SGS.Grid.Tests
{
    public class RectGridTests
    {
        [Test]
        public void Constructor_StoresGridAndCellSizes()
        {
            var grid = new RectGrid(4, 3, 2.5f, 1.25f);

            Assert.That(grid.Columns, Is.EqualTo(4));
            Assert.That(grid.Rows, Is.EqualTo(3));
            Assert.That(grid.CellWidth, Is.EqualTo(2.5f));
            Assert.That(grid.CellHeight, Is.EqualTo(1.25f));
            Assert.That(grid.CellCount, Is.EqualTo(12));
            Assert.That(grid.Width, Is.EqualTo(10f));
            Assert.That(grid.Height, Is.EqualTo(3.75f));
        }

        [Test]
        public void CoordinateToIndex_RoundTrips()
        {
            var grid = new RectGrid(5, 4, 1f, 1f);
            var coordinate = new RectGridCoordinate(3, 2);

            var index = grid.ToIndex(coordinate);
            var roundTrip = grid.GetCoordinate(index);

            Assert.That(index, Is.EqualTo(13));
            Assert.That(roundTrip, Is.EqualTo(coordinate));
        }

        [Test]
        public void CellCoordinates_AreComputedFromBottomLeftOrigin()
        {
            var grid = new RectGrid(2, 2, 3f, 4f, RectGridOrigin.BottomLeft);
            var coordinate = new RectGridCoordinate(1, 1);

            Assert.That(grid.GetCellMinCorner(coordinate), Is.EqualTo(new RectGridPoint(3f, 4f)));
            Assert.That(grid.GetCellCenter(coordinate), Is.EqualTo(new RectGridPoint(4.5f, 6f)));
            Assert.That(grid.GetCellMaxCorner(coordinate), Is.EqualTo(new RectGridPoint(6f, 8f)));
        }

        [Test]
        public void CenteredCellPosition_IsComputedFromGridCenter()
        {
            // Center origin: (0,0) = bottom-left cell, (2,1) = top-right cell.
            // X grows right, Y grows up.
            var grid = new RectGrid(3, 2, 2f, 4f);

            Assert.That(
                grid.GetCellCenterLocalPosition(new RectGridCoordinate(0, 0)),
                Is.EqualTo(new Vector3(-2f, -2f, 0f)));
            Assert.That(
                grid.GetCellCenterLocalPosition(new RectGridCoordinate(2, 1)),
                Is.EqualTo(new Vector3(2f, 2f, 0f)));
        }

        [Test]
        public void WorldPosition_RoundTripsThroughTransform()
        {
            var grid = new RectGrid(4, 3, 1.5f, 2f);
            var gridObject = new GameObject("Grid");
            var gridTransform = gridObject.transform;
            gridTransform.position = new Vector3(10f, -3f, 5f);

            try
            {
                var coordinate = new RectGridCoordinate(2, 1);
                var worldPosition = grid.GetCellCenterWorldPosition(coordinate, gridTransform);

                Assert.That(worldPosition, Is.EqualTo(new Vector3(10.75f, -3f, 5f)));
                Assert.That(grid.TryGetCoordinateFromWorldPosition(worldPosition, gridTransform, out var roundTrip), Is.True);
                Assert.That(roundTrip, Is.EqualTo(coordinate));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }

        [Test]
        public void TopLeftOrigin_UsesTopLeftAnchoredLayout()
        {
            // TopLeft origin: local (0,0) is at the top-left corner of the grid.
            // Y grows up, so row 0 (bottom) is furthest below origin.
            var grid = new RectGrid(3, 2, 2f, 4f, RectGridOrigin.TopLeft);

            Assert.That(
                grid.GetCellCenterLocalPosition(new RectGridCoordinate(0, 0)),
                Is.EqualTo(new Vector3(1f, -6f, 0f)));
            Assert.That(
                grid.GetCellCenterLocalPosition(new RectGridCoordinate(2, 1)),
                Is.EqualTo(new Vector3(5f, -2f, 0f)));
        }

        [Test]
        public void TopLeftOrigin_WorldPosition_RoundTripsThroughTransform()
        {
            // TopLeft origin, 2×2 grid, 3×4 cells. Grid pivot at top-left in local space.
            // Coord (1,1) = top row (Y=1 is above Y=0), right column.
            // Local center: X = 1*3 + 1.5 = 4.5, Y = (1-2)*4 + 2 = -2.
            // World: (-2+4.5, 5-2, 0) = (2.5, 3, 0).
            var grid = new RectGrid(2, 2, 3f, 4f, RectGridOrigin.TopLeft);
            var gridObject = new GameObject("Grid");
            var gridTransform = gridObject.transform;
            gridTransform.position = new Vector3(-2f, 5f, 0f);

            try
            {
                var coordinate = new RectGridCoordinate(1, 1);
                var worldPosition = grid.GetCellCenterWorldPosition(coordinate, gridTransform);

                Assert.That(worldPosition, Is.EqualTo(new Vector3(2.5f, 3f, 0f)));
                Assert.That(grid.TryGetCoordinateFromWorldPosition(worldPosition, gridTransform, out var roundTrip), Is.True);
                Assert.That(roundTrip, Is.EqualTo(coordinate));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }

        [Test]
        public void EnumerateCoordinates_UsesRowMajorOrder()
        {
            var grid = new RectGrid(3, 2, 1f, 1f);

            var coordinates = new[]
            {
                new RectGridCoordinate(0, 0),
                new RectGridCoordinate(1, 0),
                new RectGridCoordinate(2, 0),
                new RectGridCoordinate(0, 1),
                new RectGridCoordinate(1, 1),
                new RectGridCoordinate(2, 1)
            };

            Assert.That(grid.EnumerateCoordinates(), Is.EqualTo(coordinates));
        }

        [Test]
        public void InvalidArguments_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGrid(0, 1, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGrid(1, 0, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGrid(1, 1, 0f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RectGrid(1, 1, 1f, 0f));
        }
    }
}
