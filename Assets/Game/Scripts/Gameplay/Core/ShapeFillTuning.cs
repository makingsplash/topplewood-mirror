namespace Gameplay.Core
{
    /// <summary>
    /// Runtime tuning knobs for the shape_fill mechanic (config-over-hardcode, RFC §10). All values are
    /// the balancer's starting numbers, UNVERIFIED — tuned after playtests (θ lives per-level in LevelData).
    /// FixedStep is a start constant; determinism/reproducibility of the physics path is deferred (ADR-0001/M0).
    /// </summary>
    public sealed class ShapeFillTuning
    {
        /// <summary>ε — rest speed threshold, domain units/s (max(|lin|,|ang|·r)).</summary>
        public float RestSpeedEpsilon { get; set; } = 0.05f;

        /// <summary>T — how long a body must stay below ε before it counts as Placed.</summary>
        public float RestDurationSeconds { get; set; } = 0.5f;

        /// <summary>Fixed simulation step (s). Acceleration is by step COUNT, never by enlarging this.</summary>
        public float FixedStep { get; set; } = 1f / 60f;

        // ── L5-fix knobs (TASK-009, RFC 07 §2). Numbers are the balancer's starting values (comment 08:17). ──

        /// <summary>Casual snap-to-slot assist: on Placed, snap a piece landed within the capture window to its
        /// slot pose (game-designer: default ON everywhere; RFC 07 §2.2).</summary>
        public bool SnapToSlotEnabled { get; set; } = true;

        /// <summary>Snap capture radius (world units, > PosTol 0.25 and < half the L5 slot spacing 1.0). A piece
        /// landed grossly off-slot is NOT captured, so release ORDER stays the challenge.</summary>
        public float SnapCaptureRadius { get; set; } = 0.5f;

        /// <summary>Snap capture angle (rad, = 2× AngTol 15° = 30°). Disambiguates the two half-discs stacked at
        /// one point by rotation without being a "magnet at any angle".</summary>
        public float SnapCaptureAngle { get; set; } = 0.5236f;

        /// <summary>Linear damping applied on Release — light, does not stall the drop.</summary>
        public float ReleaseLinearDrag { get; set; } = 0.5f;

        /// <summary>Angular damping applied on Release — the main lever: kills half-disc rolling so rest is
        /// reached fast (secondary to the mandatory Freeze-on-Placed).</summary>
        public float ReleaseAngularDrag { get; set; } = 3.0f;
    }
}
