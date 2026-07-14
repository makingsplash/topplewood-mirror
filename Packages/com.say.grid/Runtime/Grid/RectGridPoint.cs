using System;

namespace SGS.Grid
{
    public readonly struct RectGridPoint : IEquatable<RectGridPoint>
    {
        public RectGridPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public bool Equals(RectGridPoint other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is RectGridPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public static bool operator ==(RectGridPoint left, RectGridPoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RectGridPoint left, RectGridPoint right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
