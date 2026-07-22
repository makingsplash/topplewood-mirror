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

        /// <summary>Isoceles triangle centred on origin, base along the bottom (CCW). Symmetry order 1.</summary>
        public static Shape Triangle(float width, float height)
        {
            var hw = width * 0.5f;
            var hh = height * 0.5f;
            return new Shape(new[]
            {
                new Vec2(-hw, -hh),
                new Vec2(hw, -hh),
                new Vec2(0f, hh),
            });
        }

        /// <summary>
        /// Upper half-disc of radius <paramref name="radius"/> (flat side along the bottom, dome up), CCW,
        /// approximated by <paramref name="segments"/> arc segments. Symmetry order 1.
        /// </summary>
        public static Shape HalfDisc(float radius, int segments = 12)
        {
            if (segments < 2)
                segments = 2;

            // segments arc points from angle 0..π, plus the flat chord closes automatically (last→first).
            var verts = new Vec2[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                var a = System.MathF.PI * i / segments;
                verts[i] = new Vec2(radius * System.MathF.Cos(a), radius * System.MathF.Sin(a));
            }
            // verts[0] = (+r,0), verts[segments] = (−r,0): points run CCW along the dome, chord closes CCW.
            return new Shape(verts);
        }
    }
}
