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
        public readonly Vec2[] Vertices; // null when this is a circle OR a compound
        public readonly Vec2 Center;     // circle centre (= the disc body's position; the collider has no offset)
        public readonly float Radius;    // >0 ⇒ circle; 0 ⇒ polygon/compound
        // TASK-178: for a CONCAVE polyomino, the world-space convex parts the physics simulates (compound body).
        // null for a circle/polygon. Every measurement (penetration, separation, support drop) is done PER PART so a
        // concave contour is never fed to the convex SAT kernel (which would report false separations/overlaps).
        public readonly Vec2[][] Parts;

        public bool IsCircle => Radius > 0f && Vertices == null && Parts == null;
        public bool IsCompound => Parts != null;

        private ConvexShape(Vec2[] verts, Vec2 center, float radius, Vec2[][] parts)
        {
            Vertices = verts;
            Center = center;
            Radius = radius;
            Parts = parts;
        }

        public static ConvexShape Polygon(Vec2[] worldVertices) => new(worldVertices, Vec2.Zero, 0f, null);

        public static ConvexShape Circle(Vec2 center, float radius) => new(null, center, radius, null);

        /// <summary>TASK-178: a compound of convex parts (world space) — the collider a concave polyomino simulates.</summary>
        public static ConvexShape Compound(Vec2[][] worldParts) => new(null, Vec2.Zero, 0f, worldParts);

        /// <summary>The convex collider the PHYSICS simulates for a shape at a pose (TASK-075/178): a native disc for a
        /// <c>Circle</c> primitive (centre = body position; a disc has no meaningful angle), a COMPOUND of convex parts
        /// for a concave polyomino (<see cref="Shape.ConvexParts"/> ≠ null), a world-vertex polygon for every other
        /// primitive. This single dispatch mirrors the adapter (UnityShapeFillSimulation.SpawnFloating) — the sim
        /// honesty detector (SimRun) AND the authored-touching slot classifier (SlotTouchClassifier) both route through
        /// here, so detector, floor classifier and physics can never diverge on how a piece is represented.</summary>
        public static ConvexShape Of(Shape shape, Vec2 position, float rotation)
        {
            if (shape.ConvexParts != null)
                return Compound(WorldParts(shape.ConvexParts, position, rotation));
            return shape.Primitive == ShapePrimitive.Circle
                ? Circle(position, shape.Radius)
                : Polygon(Geometry.WorldVertices(shape, position, rotation));
        }

        /// <summary>Copy shifted down by <paramref name="d"/> (the grounding support-probe drop): a polygon's
        /// vertices move, a circle's centre moves, a compound's every part moves. Radius is unchanged.</summary>
        public ConvexShape ShiftedDown(float d)
        {
            if (IsCompound)
            {
                var shifted = new Vec2[Parts.Length][];
                for (var i = 0; i < Parts.Length; i++)
                    shifted[i] = ShiftDownVerts(Parts[i], d);
                return Compound(shifted);
            }
            return IsCircle
                ? Circle(new Vec2(Center.X, Center.Y - d), Radius)
                : Polygon(ShiftDownVerts(Vertices, d));
        }

        private static Vec2[][] WorldParts(Vec2[][] localParts, Vec2 position, float rotation)
        {
            var world = new Vec2[localParts.Length][];
            for (var i = 0; i < localParts.Length; i++)
                world[i] = Geometry.WorldVertices(localParts[i], position, rotation);
            return world;
        }

        private static Vec2[] ShiftDownVerts(Vec2[] v, float d)
        {
            var r = new Vec2[v.Length];
            for (var i = 0; i < v.Length; i++)
                r[i] = new Vec2(v[i].X, v[i].Y - d);
            return r;
        }
    }
}
