namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Sim-side geometry helper (RFC §4/§7 п.2): the world-space centroid of a piece's shape at a given pose.
    /// The bot taps this point to address an Idle piece. The mean of the shape vertices lies strictly inside
    /// the convex MVP shapes — for a half-disc it sits above the flat chord, unlike <c>piece.Position</c> which
    /// lands on the boundary where the even-odd point-in-polygon test is unreliable. Unity-free.
    /// </summary>
    public static class SimGeometry
    {
        public static Vec2 ShapeCentroidWorld(PieceType type, Vec2 position, float rotation)
        {
            var verts = type.Shape.Vertices;
            var sum = Vec2.Zero;
            for (var i = 0; i < verts.Length; i++)
                sum += verts[i];

            var localCentroid = sum * (1f / verts.Length);
            return position + localCentroid.Rotate(rotation);
        }
    }
}
