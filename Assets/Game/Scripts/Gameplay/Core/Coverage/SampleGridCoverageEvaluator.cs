using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// Sample-grid rasterisation coverage (RFC §2.5): N×N points over the silhouette bbox; a point counts
    /// when it is inside the silhouette AND inside some settled block. Cheap, deterministic and tolerant
    /// (matches ADR-0001 "a generous threshold absorbs physics spread"). Resolution N is <c>⟨balancer/QA⟩</c>.
    /// </summary>
    public sealed class SampleGridCoverageEvaluator : ICoverageEvaluator
    {
        public float Evaluate(ISilhouetteRegion silhouette, IReadOnlyList<PlacedShape> settled, int sampleN)
        {
            if (sampleN < 2)
                sampleN = 2;

            var bbox = silhouette.Bbox;
            var w = bbox.Width;
            var h = bbox.Height;
            if (w <= 0f || h <= 0f)
                return 0f;

            var stepX = w / (sampleN - 1);
            var stepY = h / (sampleN - 1);

            var inside = 0;
            var covered = 0;

            for (var iy = 0; iy < sampleN; iy++)
            {
                var y = bbox.Min.Y + stepY * iy;
                for (var ix = 0; ix < sampleN; ix++)
                {
                    var p = new Vec2(bbox.Min.X + stepX * ix, y);
                    if (!silhouette.Contains(p))
                        continue;

                    inside++;
                    if (IsInsideAny(p, settled))
                        covered++;
                }
            }

            return inside == 0 ? 0f : covered / (float)inside;
        }

        private static bool IsInsideAny(Vec2 p, IReadOnlyList<PlacedShape> settled)
        {
            for (var i = 0; i < settled.Count; i++)
            {
                if (PointInShape(p, settled[i]))
                    return true;
            }
            return false;
        }

        private static bool PointInShape(Vec2 p, PlacedShape placed)
        {
            var n = placed.Shape.Vertices.Length;
            var inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var vi = placed.WorldVertex(i);
                var vj = placed.WorldVertex(j);
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
    }
}
