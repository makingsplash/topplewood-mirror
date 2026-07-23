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

        // ── Soft-settle knobs (TASK-009, game-balancer 13). Replace the instant snap-to-slot teleport with a
        //    short eased glide from the landing pose to a within-tolerance target, so the assist reads as the
        //    piece settling into place rather than a magnet snapping it (human feel complaint 2026-07-23 10:45). ──

        /// <summary>Duration of the eased settle glide (s). Converted to a whole FixedStep count so the glide is
        /// headless-deterministic (0.18 s ≈ 11 steps @ 1/60). game-balancer 13 §2.1 (A/B 0.12–0.25).</summary>
        public float SettleDurationSeconds { get; set; } = 0.18f;

        /// <summary>Easing curve of the glide — SmootherStep (C2) leaves rest and enters the freeze without a
        /// velocity pop. game-balancer 13 §2.2.</summary>
        public SettleEasing SettleEasing { get; set; } = SettleEasing.SmootherStep;

        /// <summary>Fraction of the landing error the glide removes (game-balancer 13 §2.3). CURRENTLY UNUSED —
        /// human 11:36 moved the settle trigger into the fall and asked for minimal correction, so position now
        /// homes to the exact slot and rotation turns only just enough to pass; the proportional-gain residual
        /// model is superseded. Kept pending balancer re-confirmation (A/B 0.70–0.85 no longer wired).</summary>
        public float SettleGain { get; set; } = 0.75f;

        /// <summary>Safety clamp on the proportional residual (game-balancer 13 §2.4). CURRENTLY UNUSED for the
        /// same reason as <see cref="SettleGain"/> — kept pending balancer re-confirmation.</summary>
        public float SettleMaxResidualFactor { get; set; } = 0.6f;
    }
}
