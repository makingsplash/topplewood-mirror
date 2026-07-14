using System;
using System.Collections.Generic;
using UnityEngine;

namespace SGS.Grid
{
    public sealed class RectGrid
    {
        public RectGrid(int columns, int rows, float cellWidth, float cellHeight)
            : this(columns, rows, cellWidth, cellHeight, RectGridOrigin.Center)
        {
        }

        public RectGrid(int columns, int rows, float cellWidth, float cellHeight, RectGridOrigin origin)
        {
            if (columns <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Grid columns must be greater than zero.");
            }

            if (rows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Grid rows must be greater than zero.");
            }

            if (cellWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellWidth), "Cell width must be greater than zero.");
            }

            if (cellHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellHeight), "Cell height must be greater than zero.");
            }

            Columns = columns;
            Rows = rows;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Origin = origin;
        }

        public int Columns { get; }

        public int Rows { get; }

        public float CellWidth { get; }

        public float CellHeight { get; }

        public RectGridOrigin Origin { get; }

        public int CellCount => Columns * Rows;

        public float Width => Columns * CellWidth;

        public float Height => Rows * CellHeight;

        public bool Contains(RectGridCoordinate coordinate)
        {
            return coordinate.X >= 0
                   && coordinate.X < Columns
                   && coordinate.Y >= 0
                   && coordinate.Y < Rows;
        }

        public int ToIndex(RectGridCoordinate coordinate)
        {
            EnsureInside(coordinate);
            return coordinate.Y * Columns + coordinate.X;
        }

        public bool TryGetCoordinate(int index, out RectGridCoordinate coordinate)
        {
            if (index < 0 || index >= CellCount)
            {
                coordinate = default;
                return false;
            }

            coordinate = new RectGridCoordinate(index % Columns, index / Columns);
            return true;
        }

        public RectGridCoordinate GetCoordinate(int index)
        {
            if (!TryGetCoordinate(index, out var coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be inside the grid.");
            }

            return coordinate;
        }

        public RectGridCoordinate GetCoordinate(int x, int y)
        {
            var coordinate = new RectGridCoordinate(x, y);
            EnsureInside(coordinate);
            return coordinate;
        }

        public RectGridPoint GetCellMinCorner(RectGridCoordinate coordinate)
        {
            EnsureInside(coordinate);
            return new RectGridPoint(
                GetCellMinCornerX(coordinate.X),
                GetCellMinCornerY(coordinate.Y));
        }

        public RectGridPoint GetCellMaxCorner(RectGridCoordinate coordinate)
        {
            EnsureInside(coordinate);
            return new RectGridPoint(
                GetCellMinCornerX(coordinate.X) + CellWidth,
                GetCellMinCornerY(coordinate.Y) + CellHeight);
        }

        [Obsolete("Use GetCellMinCorner instead.")]
        public RectGridPoint GetCellOrigin(RectGridCoordinate coordinate)
        {
            return GetCellMinCorner(coordinate);
        }

        [Obsolete("Use GetCellMaxCorner instead.")]
        public RectGridPoint GetCellTopRight(RectGridCoordinate coordinate)
        {
            return GetCellMaxCorner(coordinate);
        }

        public RectGridPoint GetCellCenter(RectGridCoordinate coordinate)
        {
            return new RectGridPoint(
                GetCellMinCornerX(coordinate.X) + CellWidth * 0.5f,
                GetCellMinCornerY(coordinate.Y) + CellHeight * 0.5f);
        }

        public Vector3 GetCellCenterLocalPosition(RectGridCoordinate coordinate)
        {
            EnsureInside(coordinate);

            return new Vector3(
                GetCellCenterLocalX(coordinate.X),
                GetCellCenterLocalY(coordinate.Y),
                0f);
        }

        public Vector3 GetCellCenterWorldPosition(RectGridCoordinate coordinate, Transform gridTransform)
        {
            if (gridTransform == null)
            {
                throw new ArgumentNullException(nameof(gridTransform));
            }

            return gridTransform.TransformPoint(GetCellCenterLocalPosition(coordinate));
        }

        public bool TryGetCoordinateFromLocalPosition(Vector3 localPosition, out RectGridCoordinate coordinate)
        {
            var column = GetColumnFromLocalX(localPosition.x);
            var row = GetRowFromLocalY(localPosition.y);
            var candidate = new RectGridCoordinate(column, row);

            if (!Contains(candidate))
            {
                coordinate = default;
                return false;
            }

            coordinate = candidate;
            return true;
        }

        public bool TryGetCoordinateFromWorldPosition(Vector3 worldPosition, Transform gridTransform, out RectGridCoordinate coordinate)
        {
            if (gridTransform == null)
            {
                throw new ArgumentNullException(nameof(gridTransform));
            }

            var localPosition = gridTransform.InverseTransformPoint(worldPosition);
            return TryGetCoordinateFromLocalPosition(localPosition, out coordinate);
        }

        public IEnumerable<RectGridCoordinate> EnumerateCoordinates()
        {
            for (var y = 0; y < Rows; y++)
            {
                for (var x = 0; x < Columns; x++)
                {
                    yield return new RectGridCoordinate(x, y);
                }
            }
        }

        private void EnsureInside(RectGridCoordinate coordinate)
        {
            if (!Contains(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate), "Coordinate must be inside the grid.");
            }
        }

        private float GetCellMinCornerX(int column)
        {
            switch (Origin)
            {
                case RectGridOrigin.Center:
                    return (column - (Columns * 0.5f)) * CellWidth;
                case RectGridOrigin.TopLeft:
                case RectGridOrigin.BottomLeft:
                    return column * CellWidth;
                case RectGridOrigin.TopRight:
                case RectGridOrigin.BottomRight:
                    return -(column + 1) * CellWidth;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Origin), Origin, "Unsupported grid origin.");
            }
        }

        private float GetCellMinCornerY(int row)
        {
            switch (Origin)
            {
                case RectGridOrigin.Center:
                    return (row - Rows * 0.5f) * CellHeight;
                case RectGridOrigin.TopLeft:
                case RectGridOrigin.TopRight:
                    return (row - Rows) * CellHeight;
                case RectGridOrigin.BottomLeft:
                case RectGridOrigin.BottomRight:
                    return row * CellHeight;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Origin), Origin, "Unsupported grid origin.");
            }
        }

        private float GetCellCenterLocalX(int column)
        {
            return GetCellMinCornerX(column) + CellWidth * 0.5f;
        }

        private float GetCellCenterLocalY(int row)
        {
            return GetCellMinCornerY(row) + CellHeight * 0.5f;
        }

        private int GetColumnFromLocalX(float localX)
        {
            switch (Origin)
            {
                case RectGridOrigin.Center:
                    return Mathf.RoundToInt(localX / CellWidth + (Columns - 1) * 0.5f);
                case RectGridOrigin.TopLeft:
                case RectGridOrigin.BottomLeft:
                    return Mathf.FloorToInt(localX / CellWidth);
                case RectGridOrigin.TopRight:
                case RectGridOrigin.BottomRight:
                    return Mathf.FloorToInt(-localX / CellWidth);
                default:
                    throw new ArgumentOutOfRangeException(nameof(Origin), Origin, "Unsupported grid origin.");
            }
        }

        private int GetRowFromLocalY(float localY)
        {
            switch (Origin)
            {
                case RectGridOrigin.Center:
                    return Mathf.FloorToInt(localY / CellHeight + Rows * 0.5f);
                case RectGridOrigin.TopLeft:
                case RectGridOrigin.TopRight:
                    return Mathf.FloorToInt(localY / CellHeight + Rows);
                case RectGridOrigin.BottomLeft:
                case RectGridOrigin.BottomRight:
                    return Mathf.FloorToInt(localY / CellHeight);
                default:
                    throw new ArgumentOutOfRangeException(nameof(Origin), Origin, "Unsupported grid origin.");
            }
        }
    }
}
