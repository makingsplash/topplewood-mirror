namespace Gameplay.Core
{
    /// <summary>
    /// Shared Unity-free geometry helpers (RFC §5.2): the single even-odd point-in-polygon test, dedup'd
    /// from the three former copies (PolygonSilhouetteRegion / SampleGridCoverageEvaluator / hit-test).
    /// </summary>
    public static class Geometry
    {
        /// <summary>Even-odd point-in-polygon over a vertex ring (world/local — caller's frame).</summary>
        public static bool PointInPolygon(Vec2[] verts, Vec2 p)
        {
            var inside = false;
            for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++)
            {
                var vi = verts[i];
                var vj = verts[j];
                if (vi.Y > p.Y != vj.Y > p.Y)
                {
                    var t = (p.Y - vi.Y) / (vj.Y - vi.Y);
                    var x = vi.X + t * (vj.X - vi.X);
                    if (p.X < x)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>A shape's local vertices transformed to world by a pose (rotate then translate).</summary>
        public static Vec2[] WorldVertices(Shape shape, Vec2 position, float rotation)
        {
            var src = shape.Vertices;
            var world = new Vec2[src.Length];
            for (var i = 0; i < src.Length; i++)
                world[i] = position + src[i].Rotate(rotation);
            return world;
        }
    }
}
