using System;

namespace SGS.Grid
{
    public readonly struct RectGridCoordinate : IEquatable<RectGridCoordinate>
    {
        public RectGridCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public bool Equals(RectGridCoordinate other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is RectGridCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(RectGridCoordinate left, RectGridCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RectGridCoordinate left, RectGridCoordinate right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
