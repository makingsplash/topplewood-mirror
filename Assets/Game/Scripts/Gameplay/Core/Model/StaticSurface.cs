namespace Gameplay.Core
{
    /// <summary>
    /// TASK-325 (S2): marks a <see cref="LevelData.StaticGeometry"/> entry as a SURFACE — an open polyline the
    /// physics traces with an <c>EdgeCollider2D</c> chain (in <c>UnityShapeFillSimulation.LoadStatics</c>) EXACTLY
    /// along the authored points, instead of the default filled solid (a <c>PolygonCollider2D</c>). This is what a
    /// smoothly-curved chute/ramp IS physically: a thin surface a ball rolls along, not a filled body. An edge chain
    /// carries no coordinate-dependent convex split (the mechanism #2 of TASK-072), so a many-vertex curved contact
    /// is device-deterministic by construction — the ≤8-vertex convex cap that binds DYNAMIC pieces does not, and must
    /// not, bind a static surface (dev-architect RFC 03, Q2).
    /// <para>
    /// A surface has no "inside": <see cref="Thickness"/> is a VIEW/authoring width — the world size of the rendered
    /// wooden ribbon (and, for the start-embed check, the ribbon the piece is measured against). The COLLIDER is the
    /// zero-width chain regardless of thickness. Absent/zero thickness falls back to <see cref="DefaultThickness"/> so
    /// the view and the validator always agree on the same ribbon.
    /// </para>
    /// </summary>
    public sealed class StaticSurface
    {
        /// <summary>Fallback ribbon width when a level authors a surface without a thickness — a visible-but-thin
        /// wooden plank. A geometric default only (any level overrides it via <c>thickness</c>); the anti-tunnel
        /// LOWER BOUND on segment length is a balancer number proven by the sim gate, not this.</summary>
        public const float DefaultThickness = 0.2f;

        /// <summary>World-units full width of the rendered ribbon (never below <see cref="DefaultThickness"/>). The
        /// physics collider is the zero-width edge chain; this only sizes the mesh and the start-embed ribbon.</summary>
        public readonly float Thickness;

        public StaticSurface(float thickness)
        {
            Thickness = thickness > 0f ? thickness : DefaultThickness;
        }
    }
}
