namespace Gameplay.Core
{
    /// <summary>
    /// Convex collider for the sim honesty detector: a polygon (<see cref="Vertices"/>) OR a circle
    /// (<see cref="Center"/> + <see cref="Radius"/>). Lets the detector measure a <c>Circle</c> piece as the
    /// NATIVE disc the physics simulates (UnityShapeFillSimulation:95 → CircleCollider2D of the authored radius)
    /// instead of <c>Shape.Circle</c>'s inscribed 24-gon, whose facet/vertex alternation is a rotation-dependent
    /// ±(R−apothem) error at the PEN_EPS scale (TASK-075). Every other primitive stays a polygon — its collider
    /// IS a polygon (box/plank/trapezoid exactly; half/quarter discs a quantized arc poly), so the polygon
    /// measurement is ALREADY consistent with the physics for them.
    /// </summary>
    public readonly struct ConvexShape
    {
        public readonly Vec2[] Vertices; // null when this is a circle
        public readonly Vec2 Center;     // circle centre (= the disc body's position; the collider has no offset)
        public readonly float Radius;    // >0 ⇒ circle; 0 ⇒ polygon

        public bool IsCircle => Radius > 0f && Vertices == null;

        private ConvexShape(Vec2[] verts, Vec2 center, float radius)
        {
            Vertices = verts;
            Center = center;
            Radius = radius;
        }

        public static ConvexShape Polygon(Vec2[] worldVertices) => new(worldVertices, Vec2.Zero, 0f);

        public static ConvexShape Circle(Vec2 center, float radius) => new(null, center, radius);

        /// <summary>Copy shifted down by <paramref name="d"/> (the grounding support-probe drop): a polygon's
        /// vertices move, a circle's centre moves. Radius is unchanged.</summary>
        public ConvexShape ShiftedDown(float d) =>
            IsCircle
                ? Circle(new Vec2(Center.X, Center.Y - d), Radius)
                : Polygon(ShiftDownVerts(Vertices, d));

        private static Vec2[] ShiftDownVerts(Vec2[] v, float d)
        {
            var r = new Vec2[v.Length];
            for (var i = 0; i < v.Length; i++)
                r[i] = new Vec2(v[i].X, v[i].Y - d);
            return r;
        }
    }
}
