using System;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// TASK-077: the honest resting penetration of a piece–piece contact, taken from the solver's OWN "solved"
    /// criterion rather than from a sampled rest point. Box2D v2.4.1 <c>b2_contact_solver.cpp</c>,
    /// <c>SolvePositionConstraints</c>, ends with <c>return minSeparation &gt;= -3.0f * b2_linearSlop;</c> — the
    /// corrector aims at <c>−linearSlop</c> and calls a contact solved within <c>3·linearSlop</c> of penetration,
    /// measured on the COLLISION radius (core + skin). The honesty detector measures CORE penetration
    /// (<see cref="Geometry.Penetration"/> on <see cref="ConvexShape"/>: disc = authored radius, polygon = vertices),
    /// so the skin is folded into the per-pair baseline here.
    /// <para>The gate (<c>sim-gate.py</c>) fails a win when <c>raw − baseline &gt; PEN_EPS</c>; "core penetration ≤
    /// 3s − Σskin" is therefore <c>baseline = 3s − Σskin − PEN_EPS = 2s − Σskin</c>, clamped at 0 and never below the
    /// authored static slot overlap. Circle–circle → 0.01; circle–polygon and polygon–polygon → 0 (byte-identical
    /// to the static-only baseline before TASK-075). A pure function of ONE run's poses and the level geometry: no
    /// cross-run floor, no sample, no dependence on piece order in the level JSON. See
    /// docs/architecture/sim-harness-metrics.md §«Предел контакта из критерия решателя». Unity-free.</para>
    /// </summary>
    public static class ContactRestAllowance
    {
        /// <summary>Box2D <c>b2_linearSlop</c>. Unity's fork is closed-source; 0.005 is confirmed by measurement —
        /// two circles at quasi-static rest sit at ≈0.005 penetration, a circle on a polygon floor rests with a
        /// +0.0049 core gap (= skin − slop). docs/platform/unity-gotchas.md §«Физика 2D».</summary>
        public const float LinearSlop = 0.005f;

        /// <summary>Collision skin of a polygon collider = Box2D <c>b2_polygonRadius</c> = <c>2·b2_linearSlop</c> =
        /// Unity <c>Physics2DSettings.m_DefaultContactOffset</c> (0.01; no collider overrides <c>contactOffset</c>).
        /// A circle has no skin — its collision radius IS its authored radius. Guarded by an EditMode test against
        /// the live project setting, so a changed offset turns a test red instead of silently moving the limit.</summary>
        public const float PolygonSkin = 2f * LinearSlop;

        /// <summary>Penetration on the collision radius the solver still calls solved: <c>3·b2_linearSlop</c>
        /// (<c>b2_contact_solver.cpp</c>: "We can't expect minSpeparation &gt;= -b2_linearSlop because we don't push
        /// the separation above -b2_linearSlop"). The ×3 is not observable directly in Unity's fork — a named
        /// hypothesis: an honest circle–circle win deeper than 0.015 is a re-validation trigger, not a reason to lift
        /// this.</summary>
        public const float SolvedPenetration = 3f * LinearSlop;

        /// <summary>The gate's margin above the baseline — must equal <c>PEN_EPS</c> in <c>.studio/tools/sim-gate.py</c>
        /// (0.005); the two change together or the limit drifts.</summary>
        public const float GateMargin = LinearSlop;

        /// <summary>The solver's honest-rest allowance above the gate margin for a skinless pair:
        /// <c>SolvedPenetration − GateMargin = 2·linearSlop</c>. Written as <c>2f · LinearSlop</c> (an exact float
        /// doubling, the same expression as <see cref="PolygonSkin"/>) so circle–polygon nets to EXACTLY 0 and stays
        /// byte-identical to the static-only baseline — a subtraction of three rounded constants would leave a ±1e-9
        /// residue there. An EditMode test pins the identity with SolvedPenetration/GateMargin.</summary>
        public const float RestAllowance = 2f * LinearSlop;

        /// <summary>Collision skin of a detector shape. Keyed on <see cref="ConvexShape.IsCircle"/> — the SAME
        /// predicate the adapter uses to build a <c>CircleCollider2D</c> (UnityShapeFillSimulation) — so detector and
        /// physics agree by construction. A compound polyomino is polygon parts → polygon skin. A new native collider
        /// kind (capsule, an <c>edgeRadius</c>) MUST be recognised here, otherwise the limit is silently wrong.</summary>
        public static float SkinOf(in ConvexShape shape) => shape.IsCircle ? 0f : PolygonSkin;

        /// <summary>Baseline the detector subtracts from a pair's raw core penetration: the authored static slot-pose
        /// overlap, or the solver's honest-rest allowance <c>RestAllowance − Σskin</c> (= <c>3s − GateMargin − Σskin</c>,
        /// clamped ≥ 0), whichever is larger — so an authored nesting deeper than the allowance keeps its own baseline
        /// and no pair is ever judged stricter than before.</summary>
        public static float Baseline(in ConvexShape a, in ConvexShape b, float staticOverlap)
            => Math.Max(staticOverlap, Math.Max(0f, RestAllowance - SkinOf(a) - SkinOf(b)));
    }
}
