using System;

namespace SGS.Grid
{
    public sealed class RectGridMap<T>
    {
        private readonly T[,] _cells;

        public RectGridMap(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

            Width = width;
            Height = height;
            _cells = new T[width, height];
        }

        // Number of cells along X (columns).
        public int Width { get; }

        // Number of cells along Y (rows).
        public int Height { get; }

        public T this[int x, int y]
        {
            get => Get(x, y);
            set => Set(x, y, value);
        }

        public T this[RectGridCoordinate coordinate]
        {
            get => Get(coordinate.X, coordinate.Y);
            set => Set(coordinate.X, coordinate.Y, value);
        }

        public T Get(int x, int y)
        {
            EnsureInside(x, y);
            return _cells[x, y];
        }

        public bool TryGet(int x, int y, out T value)
        {
            if (!IsInside(x, y))
            {
                value = default;
                return false;
            }

            value = _cells[x, y];
            return true;
        }

        public void Set(int x, int y, T value)
        {
            EnsureInside(x, y);
            _cells[x, y] = value;
        }

        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool IsInside(RectGridCoordinate coordinate)
        {
            return IsInside(coordinate.X, coordinate.Y);
        }

        private void EnsureInside(int x, int y)
        {
            if (!IsInside(x, y))
            {
                throw new ArgumentOutOfRangeException(
                    $"({x}, {y})",
                    $"Coordinate ({x}, {y}) is outside the grid [{Width} × {Height}].");
            }
        }
    }
}
