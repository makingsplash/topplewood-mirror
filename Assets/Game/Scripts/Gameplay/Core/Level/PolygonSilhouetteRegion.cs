using System;

namespace Gameplay.Core
{
    /// <summary>
    /// Polygon-backed silhouette region: even-odd point-in-polygon (outline minus holes), shoelace area,
    /// cached bounding box. Pure C#, deterministic.
    /// </summary>
    public sealed class PolygonSilhouetteRegion : ISilhouetteRegion
    {
        private readonly Polygon _polygon;

        public float Area { get; }
        public Rect Bbox { get; }

        public PolygonSilhouetteRegion(Polygon polygon)
        {
            _polygon = polygon;
            Bbox = ComputeBbox(polygon.Outline);

            var area = MathF.Abs(SignedArea(polygon.Outline));
            if (polygon.Holes != null)
            {
                foreach (var hole in polygon.Holes)
                    area -= MathF.Abs(SignedArea(hole));
            }
            Area = area;
        }

        public bool Contains(Vec2 point)
        {
            if (!InPolygon(_polygon.Outline, point))
                return false;

            if (_polygon.Holes != null)
            {
                foreach (var hole in _polygon.Holes)
                {
                    if (InPolygon(hole, point))
                        return false;
                }
            }
            return true;
        }

        private static bool InPolygon(Vec2[] verts, Vec2 p)
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

        private static float SignedArea(Vec2[] verts)
        {
            var sum = 0f;
            for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++)
                sum += (verts[j].X * verts[i].Y) - (verts[i].X * verts[j].Y);
            return sum * 0.5f;
        }

        private static Rect ComputeBbox(Vec2[] verts)
        {
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }
            return new Rect(new Vec2(minX, minY), new Vec2(maxX, maxY));
        }
    }
}
