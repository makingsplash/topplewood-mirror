using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Sim-side geometry helper (RFC §4/§7 п.2): the world-space centroid of a piece's shape at a given pose.
    /// The bot taps this point to address an Idle piece. The mean of the shape vertices lies strictly inside
    /// the convex MVP shapes — for a half-disc it sits above the flat chord, unlike <c>piece.Position</c> which
    /// lands on the boundary where the even-odd point-in-polygon test is unreliable. Unity-free.
    /// <para>TASK-178: for a CONCAVE polyomino the mean of the contour vertices can fall in the notch (OUTSIDE the
    /// body — e.g. an L's), where the even-odd hit-test rejects it and the bot cannot address the piece. For those we
    /// return the centre of the LARGEST convex part, which is guaranteed strictly inside the silhouette.</para>
    /// </summary>
    public static class SimGeometry
    {
        /// <summary>
        /// TASK-645: the point the bot taps to address <paramref name="piece"/>. The tap goes through the same
        /// <see cref="PieceHitResolver"/> a player's does, and that resolver now gives the point to the piece drawn ON TOP
        /// — so the centroid of a piece partly under another may release the cover instead. Rule: the centroid when it
        /// resolves to the piece (every level without such an overlap — the sweep stays bit-for-bit), else the deepest
        /// point of the piece's visible part (<see cref="PieceHitResolver.FindAim"/>). A piece covered entirely has no
        /// such point: the bot taps the centroid and releases the cover, exactly what a player's tap there would do.
        /// </summary>
        public static Vec2 TapPoint(GameModel model, PieceInstance piece)
        {
            var centroid = ShapeCentroidWorld(model.TypeFor(piece), piece.Position, piece.Rotation);
            var candidates = new List<PieceHitResolver.HitCandidate>();
            foreach (var p in model.Pieces)
            {
                if (p.State != PieceLifecycle.Idle)
                    continue;
                var world = Geometry.WorldVertices(model.TypeFor(p).Shape, p.Position, p.Rotation);
                candidates.Add(new PieceHitResolver.HitCandidate(p.InstanceId, p.PlacementIndex, world, p.Position));
            }

            if (PieceHitResolver.Resolve(candidates, centroid, 0f) == piece.InstanceId)
                return centroid;
            return PieceHitResolver.FindAim(candidates, piece.InstanceId, out var aim) == PieceHitResolver.Visibility.Hidden
                ? centroid
                : aim;
        }

        public static Vec2 ShapeCentroidWorld(PieceType type, Vec2 position, float rotation)
        {
            var localCentroid = type.Shape.ConvexParts != null
                ? LargestPartCentroid(type.Shape.ConvexParts)
                : VertexMean(type.Shape.Vertices);
            return position + localCentroid.Rotate(rotation);
        }

        private static Vec2 VertexMean(Vec2[] verts)
        {
            var sum = Vec2.Zero;
            for (var i = 0; i < verts.Length; i++)
                sum += verts[i];
            return sum * (1f / verts.Length);
        }

        /// <summary>Centre of the largest-area convex part — always strictly inside that part, hence inside the whole
        /// concave silhouette. Deterministic tie-break (first-wins on equal area) keeps the tap point stable.</summary>
        private static Vec2 LargestPartCentroid(Vec2[][] parts)
        {
            var bestArea = -1f;
            var best = Vec2.Zero;
            foreach (var part in parts)
            {
                var area = PolygonArea(part);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = VertexMean(part);
                }
            }
            return best;
        }

        /// <summary>Absolute shoelace area of a simple polygon.</summary>
        private static float PolygonArea(Vec2[] v)
        {
            var sum = 0f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                sum += v[j].X * v[i].Y - v[i].X * v[j].Y;
            return System.MathF.Abs(sum) * 0.5f;
        }
    }
}
