namespace Gameplay.Core
{
    /// <summary>Axis-aligned rectangle in domain space (min/max corners). Unity-free.</summary>
    public readonly struct Rect
    {
        public readonly Vec2 Min;
        public readonly Vec2 Max;

        public Rect(Vec2 min, Vec2 max)
        {
            Min = min;
            Max = max;
        }

        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
        public bool IsDegenerate => Width <= 0f || Height <= 0f;

        public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
    }
}
