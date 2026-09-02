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
        // TASK-178: concave tetrominoes. Unlike every primitive above (convex, one PolygonCollider2D) these carry a
        // <see cref="Shape.ConvexParts"/> authored decomposition — the collider is compound and the honesty detector
        // measures per part, so Box2D never re-splits a concave contour by a coordinate-dependent split (TASK-072).
        // J is the mirror of L and Z the mirror of S — DISTINCT primitives (physics has no reflection, cf. RightTriangle).
        LTetromino,
        JTetromino,
        STetromino,
        ZTetromino,
        TTetromino,
        // TASK-314: concave PENTOMINOES (5 cells). Same generic ConvexParts path as the tetrominoes above — the
        // heavy machinery (compound collider, per-part SAT, per-fragment mesh fan) is cell-count-agnostic, so these
        // cost only an enum value + a one-line factory + a DTO case each. Appended at the END so existing ordinals
        // do not shift (though nothing hashes the ordinal — poseHash never reads Primitive). Chirality, like L↔J /
        // S↔Z: N, P and Z(=`ZPentomino`) are chiral — their mirrors (N′, P′, S-pentomino) are DISTINCT future
        // primitives, NOT added here (physics has no reflection). T, V, plus are achiral.
        TPentomino,
        NPentomino,
        ZPentomino,
        VPentomino,
        PPentomino,
        PlusPentomino,
        // TASK-330: L-pentomino (a 4-cell vertical bar + a foot). Same generic ConvexParts path as the pentominoes
        // above — cell-count-agnostic machinery, so it costs an enum value + a one-line factory + a DTO case. Appended
        // at the END so existing ordinals do not shift (nothing hashes the ordinal — poseHash never reads Primitive).
        // Chiral (as L↔J tetromino): its mirror is the J-pentomino, a DISTINCT future primitive, NOT added here
        // (physics has no reflection). Symmetry order 1.
        LPentomino,
    }

    /// <summary>
    /// Convex block geometry in local space (vertices, CCW). v0 restricts blocks to convex
    /// polygons / primitives (RFC R4) so a single <c>PolygonCollider2D</c> maps cleanly and the
    /// point-in-shape test used by coverage stays a cheap convex check.
    /// Catalogue: box, triangle, halfdisc, plank, trapezoid, parallelogram, righttriangle, quarterdisc,
    /// gable, circle — all convex, one <c>PolygonCollider2D</c> each. Concave polyominoes — tetrominoes (L/J/S/Z/T,
    /// TASK-178) and pentominoes (T/N/Z/V/P/plus, TASK-314; L, TASK-330) — are implemented via an authored integer
    /// <see cref="ConvexParts"/> decomposition (compound collider), NOT a runtime concave split.
    /// </summary>
    public sealed class Shape
    {
        public Vec2[] Vertices { get; }

        /// <summary>TASK-178: authored convex decomposition of a CONCAVE primitive (the tetrominoes) — a set of
        /// axis-aligned convex parts (each ≤4 vertices, CCW) whose union is exactly <see cref="Vertices"/>. <c>null</c>
        /// for every convex primitive (all the ones above the tetrominoes), so their code paths are byte-for-byte
        /// unchanged. It is the SINGLE source both the physics collider (a compound body of one PolygonCollider2D per
        /// part, <c>UnityShapeFillSimulation.SpawnFloating</c>) and the honesty detector (per-part SAT, <c>SimRun</c>)
        /// read — so collider and detector can never diverge (the single-source lesson of TASK-074/075). The parts are
        /// exact products of the authored cell size (no trig), so a compound body's auto-summed mass/inertia is
        /// platform-invariant BY CONSTRUCTION — stricter than the native circle (TASK-072), and the reason a concave
        /// contour never reaches Box2D's coordinate-dependent convex split.</summary>
        public Vec2[][] ConvexParts { get; }

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

        public Shape(Vec2[] vertices, ShapePrimitive primitive, float radius = 0f, Vec2[][] convexParts = null)
        {
            Vertices = vertices;
            Primitive = primitive;
            Radius = radius;
            ConvexParts = convexParts;
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

        // ---------------------------------------------------------------- TASK-178: concave tetrominoes
        //
        // Each tetromino is authored on an INTEGER 1-cell grid (four unit cells). From that grid we build, in one
        // pure-integer pass (no trig, no runtime decomposition):
        //   • Vertices    — the full concave silhouette contour (CCW), for the mesh / silhouette / point-in-shape;
        //   • ConvexParts — one axis-aligned unit-square part per cell (each ≤4 verts, convex), union == the contour.
        // Both are then centred on the shape's bbox and scaled by <paramref name="cell"/> (world units per grid cell),
        // so the local frame matches the convex primitives (bbox centred on origin). Author-time integers ⇒ every
        // vertex is an exact product of <paramref name="cell"/> ⇒ the compound body's mass/inertia are platform-
        // invariant by construction (TASK-072 §1). Grid layouts (col,row), y-up:
        //   L: (0,0)(0,1)(0,2)(1,0)   J: (1,0)(1,1)(1,2)(0,0)   S: (0,0)(1,0)(1,1)(2,1)
        //   Z: (1,0)(2,0)(0,1)(1,1)   T: (0,0)(1,0)(2,0)(1,1)
        //
        // TASK-314: concave PENTOMINOES (5 cells) — same integer construction, same generic path. Grids (col,row), y-up:
        //   T5:   (2,0)(0,1)(1,1)(2,1)(2,2)      N5: (1,0)(2,0)(3,0)(0,1)(1,1)   Z5: (1,0)(2,0)(1,1)(0,2)(1,2)
        //   V5:   (0,0)(1,0)(2,0)(2,1)(2,2)      P5: (0,0)(1,0)(0,1)(1,1)(0,2)   +5: (1,0)(0,1)(1,1)(2,1)(1,2)
        //   Chirality (as L↔J / S↔Z): N, P, Z5 are chiral — mirrors N′/P′/S-pentomino are DISTINCT future primitives,
        //   not derivable by rotation, NOT added here. T5, V5, +5 are achiral.

        /// <summary>L-tetromino (vertical bar of 3 + a foot at the bottom-right). Chiral — J is its mirror, a DISTINCT
        /// primitive (physics has no reflection). <paramref name="cell"/> = world size of one grid cell. Symmetry order 1.</summary>
        public static Shape LTetromino(float cell)
            => Polyomino(ShapePrimitive.LTetromino, cell,
                new[] { (0, 0), (0, 1), (0, 2), (1, 0) },
                new[] { (0, 0), (2, 0), (2, 1), (1, 1), (1, 3), (0, 3) });

        /// <summary>J-tetromino (vertical bar of 3 + a foot at the bottom-left) — the mirror of <see cref="LTetromino"/>.
        /// Symmetry order 1.</summary>
        public static Shape JTetromino(float cell)
            => Polyomino(ShapePrimitive.JTetromino, cell,
                new[] { (1, 0), (1, 1), (1, 2), (0, 0) },
                new[] { (0, 0), (2, 0), (2, 3), (1, 3), (1, 1), (0, 1) });

        /// <summary>S-tetromino (bottom pair shifted left of the top pair). Chiral — Z is its mirror. Symmetry order 2
        /// (self-coincides at 180°). Symmetry order authored in the level data, not inferred here.</summary>
        public static Shape STetromino(float cell)
            => Polyomino(ShapePrimitive.STetromino, cell,
                new[] { (0, 0), (1, 0), (1, 1), (2, 1) },
                new[] { (0, 0), (2, 0), (2, 1), (3, 1), (3, 2), (1, 2), (1, 1), (0, 1) });

        /// <summary>Z-tetromino — the mirror of <see cref="STetromino"/>. Symmetry order 2.</summary>
        public static Shape ZTetromino(float cell)
            => Polyomino(ShapePrimitive.ZTetromino, cell,
                new[] { (1, 0), (2, 0), (0, 1), (1, 1) },
                new[] { (1, 0), (3, 0), (3, 1), (2, 1), (2, 2), (0, 2), (0, 1), (1, 1) });

        /// <summary>T-tetromino (a 3-cell base with one cell centred on top). Symmetry order 1. Symmetry order authored
        /// in the level data.</summary>
        public static Shape TTetromino(float cell)
            => Polyomino(ShapePrimitive.TTetromino, cell,
                new[] { (0, 0), (1, 0), (2, 0), (1, 1) },
                new[] { (0, 0), (3, 0), (3, 1), (2, 1), (2, 2), (1, 2), (1, 1), (0, 1) });

        // ---------------------------------------------------------------- TASK-314: concave pentominoes (5 cells)

        /// <summary>T-pentomino (a 3-cell column with a 3-cell foot spanning off its bottom). Achiral. Symmetry order 1
        /// (authored in the level data).</summary>
        public static Shape TPentomino(float cell)
            => Polyomino(ShapePrimitive.TPentomino, cell,
                new[] { (2, 0), (0, 1), (1, 1), (2, 1), (2, 2) },
                new[] { (2, 0), (3, 0), (3, 3), (2, 3), (2, 2), (0, 2), (0, 1), (2, 1) });

        /// <summary>N-pentomino (offset 3+2 zig). Chiral — its mirror N′ is a DISTINCT future primitive (physics has no
        /// reflection). Symmetry order 1.</summary>
        public static Shape NPentomino(float cell)
            => Polyomino(ShapePrimitive.NPentomino, cell,
                new[] { (1, 0), (2, 0), (3, 0), (0, 1), (1, 1) },
                new[] { (1, 0), (4, 0), (4, 1), (2, 1), (2, 2), (0, 2), (0, 1), (1, 1) });

        /// <summary>Z-pentomino (full central column + two cells at opposite corners — a zig-zag). Chiral — its mirror is
        /// the S-pentomino, a DISTINCT future primitive. Symmetry order 2 (self-coincides at 180°); MUST be authored
        /// with <c>symmetryOrder: 2</c> or the pose judge rejects a valid 180°-rotated landing (cf. s/z_tetromino).</summary>
        public static Shape ZPentomino(float cell)
            => Polyomino(ShapePrimitive.ZPentomino, cell,
                new[] { (1, 0), (2, 0), (1, 1), (0, 2), (1, 2) },
                new[] { (1, 0), (3, 0), (3, 1), (2, 1), (2, 3), (0, 3), (0, 2), (1, 2) });

        /// <summary>V-pentomino (two 3-cell legs meeting in an L at the bottom-right corner). Achiral. Symmetry order 1.</summary>
        public static Shape VPentomino(float cell)
            => Polyomino(ShapePrimitive.VPentomino, cell,
                new[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2) },
                new[] { (0, 0), (3, 0), (3, 3), (2, 3), (2, 1), (0, 1) });

        /// <summary>P-pentomino (a 2×2 block with one extra cell atop its left column). Chiral — its mirror P′ is a
        /// DISTINCT future primitive. Symmetry order 1.</summary>
        public static Shape PPentomino(float cell)
            => Polyomino(ShapePrimitive.PPentomino, cell,
                new[] { (0, 0), (1, 0), (0, 1), (1, 1), (0, 2) },
                new[] { (0, 0), (2, 0), (2, 2), (1, 2), (1, 3), (0, 3) });

        /// <summary>Plus/X-pentomino (a centre cell with one cell on each of its four sides). Achiral, 4-fold rotational
        /// symmetry — author <c>symmetryOrder: 4</c>. </summary>
        public static Shape PlusPentomino(float cell)
            => Polyomino(ShapePrimitive.PlusPentomino, cell,
                new[] { (1, 0), (0, 1), (1, 1), (2, 1), (1, 2) },
                new[] { (1, 0), (2, 0), (2, 1), (3, 1), (3, 2), (2, 2), (2, 3), (1, 3), (1, 2), (0, 2), (0, 1), (1, 1) });

        /// <summary>L-pentomino (a 4-cell vertical bar with a foot at the bottom-right — the pentomino extension of
        /// <see cref="LTetromino"/>). Grid (col,row), y-up: (0,0)(0,1)(0,2)(0,3)(1,0). Chiral — its mirror is the
        /// J-pentomino, a DISTINCT future primitive (physics has no reflection). Symmetry order 1.</summary>
        public static Shape LPentomino(float cell)
            => Polyomino(ShapePrimitive.LPentomino, cell,
                new[] { (0, 0), (0, 1), (0, 2), (0, 3), (1, 0) },
                new[] { (0, 0), (2, 0), (2, 1), (1, 1), (1, 4), (0, 4) });

        /// <summary>Build a concave polyomino from its integer <paramref name="cells"/> (one unit-square part each) and
        /// its integer <paramref name="contour"/> (CCW silhouette). Both are centred on the cells' bbox and scaled by
        /// <paramref name="cell"/>. Pure integer arithmetic → exact, platform-invariant vertices (TASK-072).</summary>
        private static Shape Polyomino(ShapePrimitive primitive, float cell,
            (int X, int Y)[] cells, (int X, int Y)[] contour)
        {
            // bbox over the cells' corners: [minCol, maxCol+1] × [minRow, maxRow+1].
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in cells)
            {
                if (c.X < minX) minX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.X + 1 > maxX) maxX = c.X + 1;
                if (c.Y + 1 > maxY) maxY = c.Y + 1;
            }
            var centreX = (minX + maxX) * 0.5f;
            var centreY = (minY + maxY) * 0.5f;

            Vec2 World(float gx, float gy) => new Vec2((gx - centreX) * cell, (gy - centreY) * cell);

            var verts = new Vec2[contour.Length];
            for (var i = 0; i < contour.Length; i++)
                verts[i] = World(contour[i].X, contour[i].Y);

            var parts = new Vec2[cells.Length][];
            for (var i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                parts[i] = new[]
                {
                    World(c.X, c.Y),
                    World(c.X + 1, c.Y),
                    World(c.X + 1, c.Y + 1),
                    World(c.X, c.Y + 1),
                };
            }

            return new Shape(verts, primitive, 0f, parts);
        }
    }
}
