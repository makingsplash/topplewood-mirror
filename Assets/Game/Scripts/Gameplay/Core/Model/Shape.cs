namespace Gameplay.Core
{
    /// <summary>
    /// Convex block geometry in local space (vertices, CCW). v0 restricts blocks to convex
    /// polygons / primitives (RFC R4) so a single <c>PolygonCollider2D</c> maps cleanly and the
    /// point-in-shape test used by coverage stays a cheap convex check.
    /// </summary>
    public sealed class Shape
    {
        public Vec2[] Vertices { get; }

        public Shape(Vec2[] vertices)
        {
            Vertices = vertices;
        }

        /// <summary>Axis-aligned box centred on origin.</summary>
        public static Shape Box(float width, float height)
        {
            var hw = width * 0.5f;
            var hh = height * 0.5f;
            return new Shape(new[]
            {
                new Vec2(-hw, -hh),
                new Vec2(hw, -hh),
                new Vec2(hw, hh),
                new Vec2(-hw, hh),
            });
        }
    }
}
