namespace Gameplay.Core
{
    /// <summary>Which primitive a <see cref="Shape"/> was authored as. Carried into the domain so the physics
    /// adapter can pick a NATIVE collider (a circle → <c>CircleCollider2D</c>) from the authored radius instead
    /// of re-deriving geometry from trig-generated vertices — see <see cref="Shape.Radius"/> and TASK-072.</summary>
    public enum ShapePrimitive
    {
        Box,
        Triangle,
        HalfDisc,
        Plank,
        Circle,
        Trapezoid,
        Parallelogram,
        RightTriangle,
        QuarterDisc,
        Gable,
    }

    /// <summary>
    /// Convex block geometry in local space (vertices, CCW). v0 restricts blocks to convex
    /// polygons / primitives (RFC R4) so a single <c>PolygonCollider2D</c> maps cleanly and the
    /// point-in-shape test used by coverage stays a cheap convex check.
    /// Catalogue: box, triangle, halfdisc, plank, trapezoid, parallelogram, righttriangle, quarterdisc,
    /// gable, circle — all convex; concave polyominoes (L/T/S/Z/J) are a future increment (multi-path collider +
    /// ear-clipping mesh).
    /// </summary>
    public sealed class Shape
    {
        public Vec2[] Vertices { get; }

        /// <summary>The primitive this shape was authored as. Lets consumers dispatch on shape family without
        /// re-inferring it from the vertex soup (TASK-072).</summary>
        public ShapePrimitive Primitive { get; }

        /// <summary>Authored radius for round primitives (Circle/HalfDisc/QuarterDisc); <c>0</c> for straight-edged
        /// ones. The physics adapter builds a native <c>CircleCollider2D</c> from THIS value for a circle instead
        /// of from <see cref="Vertices"/> — the vertices are produced by runtime trig (<c>MathF.Cos/Sin</c>), whose
        /// implementation differs between Mono (editor) and IL2CPP (arm64), so a vertex-derived circle collider is a
        /// platform-dependent physics input (TASK-072). Vertices are still generated for coverage/mesh.</summary>
        public float Radius { get; }

        // TASK-072: snap trig-generated vertices to a fixed grid so a last-bit difference between the Mono and
        // IL2CPP MathF.Cos/Sin implementations cannot make the collider polygon (and hence contact geometry)
        // platform-dependent. 1e-4 world units is ~3 orders below the coarsest match tolerance, so it is invisible
        // to gameplay/coverage but larger than any float-implementation gap. Straight-edged primitives need no
        // quantization — their vertices are exact products of the authored dimensions.
        private const float VertexQuantum = 1e-4f;

        public Shape(Vec2[] vertices, ShapePrimitive primitive, float radius = 0f)
        {
            Vertices = vertices;
            Primitive = primitive;
            Radius = radius;
        }

        private static Vec2 Quantize(float x, float y)
            => new Vec2(
                System.MathF.Round(x / VertexQuantum) * VertexQuantum,
                System.MathF.Round(y / VertexQuantum) * VertexQuantum);

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
            }, ShapePrimitive.Box);
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
            }, ShapePrimitive.Triangle);
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
                verts[i] = Quantize(radius * System.MathF.Cos(a), radius * System.MathF.Sin(a));
            }
            // verts[0] = (+r,0), verts[segments] = (−r,0): points run CCW along the dome, chord closes CCW.
            return new Shape(verts, ShapePrimitive.HalfDisc, radius);
        }

        /// <summary>Rectangle plank (I-beam / 1×2 domino). Same geometry family as <see cref="Box"/>; a distinct
        /// primitive/kind for authoring clarity and its own symmetry (order 2, not 4). CCW.</summary>
        public static Shape Plank(float width, float height)
        {
            var box = Box(width, height);
            return new Shape(box.Vertices, ShapePrimitive.Plank);
        }

        /// <summary>
        /// Full disc of radius <paramref name="radius"/>, approximated by a regular <paramref name="segments"/>-gon
        /// (CCW, NO centre vertex — a clean convex N-gon, so the single <c>PolygonCollider2D</c> and the convex-fan
        /// mesh apply exactly as for every other primitive; nothing in sim/mesh is special-cased, TASK-031). Author a
        /// matching high <c>symmetryOrder</c> (e.g. 24) so the pose match ignores rotation (a round piece has no
        /// meaningful angle). Valid for radius &gt; 0; segments is clamped to ≥3.
        /// </summary>
        public static Shape Circle(float radius, int segments = 24)
        {
            if (segments < 3)
                segments = 3;

            var verts = new Vec2[segments];
            for (var i = 0; i < segments; i++)
            {
                var a = 2f * System.MathF.PI * i / segments; // 0..2π, CCW
                verts[i] = Quantize(radius * System.MathF.Cos(a), radius * System.MathF.Sin(a));
            }
            // Radius is carried so the physics adapter uses a native CircleCollider2D (exact, platform-invariant)
            // rather than a PolygonCollider2D from these trig vertices (TASK-072); the vertices remain for the
            // convex-fan mesh and the coverage test.
            return new Shape(verts, ShapePrimitive.Circle, radius);
        }

        /// <summary>Isosceles trapezoid, bbox centred on origin (<paramref name="bottomWidth"/> along the bottom),
        /// CCW. Convex for positive dims. Symmetry order 1.</summary>
        public static Shape Trapezoid(float bottomWidth, float topWidth, float height)
        {
            var hb = bottomWidth * 0.5f;
            var ht = topWidth * 0.5f;
            var hh = height * 0.5f;
            return new Shape(new[]
            {
                new Vec2(-hb, -hh),
                new Vec2(hb, -hh),
                new Vec2(ht, hh),
                new Vec2(-ht, hh),
            }, ShapePrimitive.Trapezoid);
        }

        /// <summary>Parallelogram = rectangle sheared along X by <paramref name="skew"/> (top edge shifted).
        /// Local frame = the pre-shear rectangle centre; centroid sits at (skew/2, 0). CCW. Symmetry order 2.</summary>
        public static Shape Parallelogram(float width, float height, float skew)
        {
            var hw = width * 0.5f;
            var hh = height * 0.5f;
            return new Shape(new[]
            {
                new Vec2(-hw, -hh),
                new Vec2(hw, -hh),
                new Vec2(hw + skew, hh),
                new Vec2(-hw + skew, hh),
            }, ShapePrimitive.Parallelogram);
        }

        /// <summary>Right triangle (wedge): the right angle sits at the bottom-left of the bbox,
        /// legs along the axes, hypotenuse from bottom-right to top-left. bbox centred on origin, CCW.
        /// Chiral (no reflection symmetry). Symmetry order 1.</summary>
        public static Shape RightTriangle(float width, float height)
        {
            var hw = width * 0.5f;
            var hh = height * 0.5f;
            return new Shape(new[]
            {
                new Vec2(-hw, -hh), // right-angle corner (bottom-left)
                new Vec2(hw, -hh),  // bottom-right
                new Vec2(-hw, hh),  // top-left
            }, ShapePrimitive.RightTriangle);
        }

        /// <summary>Quarter disc (90° sector): the right-angle corner (arc centre) sits at the bottom-left
        /// of the bbox; two straight radii run along +X and +Y, the arc bulges into the top-right quadrant.
        /// bbox (radius×radius) centred on origin, CCW, arc approximated by <paramref name="segments"/> segments.
        /// Chiral. Symmetry order 1.</summary>
        public static Shape QuarterDisc(float radius, int segments = 12)
        {
            if (segments < 2)
                segments = 2;

            var h = radius * 0.5f;
            var corner = new Vec2(-h, -h);            // arc centre == right-angle corner, bottom-left of bbox
            var verts = new Vec2[segments + 2];
            verts[0] = corner;
            for (var i = 0; i <= segments; i++)
            {
                var a = System.MathF.PI * 0.5f * i / segments; // 0 .. π/2
                verts[i + 1] = Quantize(corner.X + radius * System.MathF.Cos(a),
                                        corner.Y + radius * System.MathF.Sin(a));
            }
            // corner → arc[a=0]=(+h,−h) → … → arc[a=π/2]=(−h,+h) → close to corner: two radii + arc, CCW.
            // Radius is carried for symmetry with the other round primitives; the quarter-disc keeps a POLYGON
            // collider (no native Unity quarter-circle) — quantized vertices make that polygon platform-invariant.
            return new Shape(verts, ShapePrimitive.QuarterDisc, radius);
        }

        /// <summary>Gable (house/pentagon, NOT regular): a rectangular base (width × height) topped by a
        /// triangular roof of <paramref name="apexHeight"/>, apex point-up. bbox (width × (height+apexHeight))
        /// centred on origin, CCW. Vertical mirror axis only (reflection, not available to physics) ⇒ rotational
        /// symmetry order 1. Convex for positive dims.</summary>
        public static Shape Gable(float width, float height, float apexHeight)
        {
            var hw = width * 0.5f;
            var half = (height + apexHeight) * 0.5f; // half of the full height
            var eaveY = -half + height;               // top of the rectangular part (roof eaves)
            return new Shape(new[]
            {
                new Vec2(-hw, -half),   // base bottom-left
                new Vec2(hw, -half),    // base bottom-right
                new Vec2(hw, eaveY),    // eave right
                new Vec2(0f, half),     // apex (point-up)
                new Vec2(-hw, eaveY),   // eave left
            }, ShapePrimitive.Gable);
        }
    }
}
