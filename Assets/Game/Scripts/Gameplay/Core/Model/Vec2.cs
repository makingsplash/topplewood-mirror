using System;

namespace Gameplay.Core
{
    /// <summary>
    /// Unity-free 2D vector for the headless-simulatable domain (see docs/architecture/overview.md).
    /// Mirrors the discipline of <c>InputIntent</c>: no <c>UnityEngine.Vector2</c> in domain code.
    /// Vec2 &lt;-&gt; Vector2 conversion lives only on the Unity adapter side.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);

        public float SqrLength => X * X + Y * Y;
        public float Length => MathF.Sqrt(SqrLength);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Rotate around origin by <paramref name="radians"/> (CCW positive).</summary>
        public Vec2 Rotate(float radians)
        {
            var c = MathF.Cos(radians);
            var s = MathF.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
