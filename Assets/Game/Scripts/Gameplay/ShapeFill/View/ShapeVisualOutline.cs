using Gameplay.Core;

namespace Gameplay.View
{
    /// <summary>
    /// View-only tessellation of a domain <see cref="Shape"/>'s outline, used purely for RENDERING (mesh +
    /// silhouette). It never touches physics.
    /// <para>
    /// Arc primitives (HalfDisc/QuarterDisc) carry a deliberately coarse ≤8-vertex outline in
    /// <see cref="Shape.Vertices"/>: TASK-072/074 proved Box2D re-splits a &gt;8-vertex polygon by a
    /// coordinate-dependent split that is not bit-stable between the Mono editor and IL2CPP arm64, so the
    /// PHYSICS collider must stay at 8 vertices (a half-disc dome across 180° in 7 facets). Drawing the mesh
    /// straight from those 8 vertices looks visibly faceted (~26° joint angle on a wheel). The VIEW therefore
    /// re-tessellates the arc from <see cref="Shape.Primitive"/> + <see cref="Shape.Radius"/> at a higher,
    /// view-only segment count (<see cref="VisualArcSegments"/>), reusing the same domain arc factory so there
    /// is no second trig implementation.
    /// </para>
    /// <para>
    /// INVARIANT — do NOT "fix" this back by reducing the view to <see cref="Shape.Vertices"/>: the collider
    /// (<c>UnityShapeFillSimulation.SpawnFloating</c>) and the honesty detector (<c>SimRun.ConvexPenetration</c>)
    /// must keep reading the SAME <see cref="Shape.Vertices"/> array — that pair both judges physics and MUST
    /// stay a single source (splitting it is exactly the collider≠detector defect TASK-075 caught on circles).
    /// The view is safe to diverge because it feeds neither collider nor detector nor verdict.
    /// </para>
    /// Straight-edged primitives are exact — their vertices are the true outline, so they pass through unchanged.
    /// </summary>
    public static class ShapeVisualOutline
    {
        /// <summary>Facets across a half-disc's 180° dome for the VIEW. 180°/24 = 7.5° joint angle (≤ 8°, the
        /// smoothness bar for TASK-074) — indistinguishable from a smooth arc at wheel radius. The quarter-disc
        /// (90°) uses half as many for the same joint angle. This is a VIEW constant only; the physics vertex
        /// count lives in the level data (segments:7 ⇒ 8 collider vertices) and is enforced by
        /// <c>LevelDataValidator</c>, which reads <see cref="Shape.Vertices"/>, not this.</summary>
        public const int VisualArcSegments = 24;

        /// <summary>The outline the view should render for <paramref name="shape"/>: a re-tessellated arc for
        /// dome primitives, or the exact vertices for straight-edged ones.</summary>
        public static Vec2[] Build(Shape shape)
        {
            switch (shape.Primitive)
            {
                // Reuse the domain arc factory (single source of arc geometry) at the higher view resolution.
                // Circle is intentionally NOT here: its physics collider is a native circle (radius-exact) and
                // its authored N-gon vertices already render smooth, so it needs no view-only re-tessellation.
                case ShapePrimitive.HalfDisc:
                    return Shape.HalfDisc(shape.Radius, VisualArcSegments).Vertices;
                case ShapePrimitive.QuarterDisc:
                    return Shape.QuarterDisc(shape.Radius, VisualArcSegments / 2).Vertices;
                default:
                    return shape.Vertices;
            }
        }
    }
}
