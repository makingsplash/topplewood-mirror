namespace Gameplay.Core
{
    /// <summary>A settled block's geometry placed in world space, consumed by coverage evaluation.</summary>
    public readonly struct PlacedShape
    {
        public readonly Shape Shape;
        public readonly Vec2 Position;
        public readonly float Rotation;

        public PlacedShape(Shape shape, Vec2 position, float rotation)
        {
            Shape = shape;
            Position = position;
            Rotation = rotation;
        }

        /// <summary>Local vertex transformed to world space.</summary>
        public Vec2 WorldVertex(int i) => Position + Shape.Vertices[i].Rotate(Rotation);
    }
}
