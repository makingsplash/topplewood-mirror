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

        /// <summary>Snap capture radius (world units). Kept in (PosTol 0.25, half the L5 slot spacing 1.0): a
        /// piece landed grossly off-slot is NOT captured, so release ORDER stays the challenge, and it can never
        /// reach a neighbouring slot. Lowered 0.5→0.3 so the assist engages nearer the slot instead of "starting
        /// too early" while the piece is still well above it (human 12:02) — balancer to re-bless the value and
        /// re-check L5 half-disc capture.</summary>
        public float SnapCaptureRadius { get; set; } = 0.3f;

        /// <summary>Snap capture angle (rad, = 2× AngTol 15° = 30°). Disambiguates the two half-discs stacked at
        /// one point by rotation without being a "magnet at any angle".</summary>
        public float SnapCaptureAngle { get; set; } = 0.5236f;

        /// <summary>Support-probe gap (world units) for the snap gate (TASK-010, human 2026-07-23 15:20). Snap
        /// captures a slot only once the piece has support this close beneath its lower edge (floor or an
        /// already-settled body) — i.e. it is about to sit down, not still free-falling. Kept small so a piece
        /// falling through a higher same-Kind slot's capture window (empty space below) keeps falling to its own
        /// lower slot instead of being grabbed mid-air. Physical & level-agnostic: no dependence on neighbouring
        /// slots' Kind/position. Near landing a piece advances well under this per fixed step, so the gate opens
        /// on the contact frame; QA/balancer to re-bless.</summary>
        public float SnapSupportProbeDistance { get; set; } = 0.1f;

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

        /// <summary>Easing curve of the settle — now drives the ROTATION correction only (game-balancer 13 §2.2).
        /// Position no longer uses it: since the settle begins mid-fall (human 11:36), position is driven by a
        /// critically-damped approach that preserves the fall velocity so there is no entry hitch (human 12:02),
        /// which supersedes the fixed-curve position glide.</summary>
        public SettleEasing SettleEasing { get; set; } = SettleEasing.SmootherStep;

        /// <summary>Fraction of the landing error the glide removes (game-balancer 13 §2.3). CURRENTLY UNUSED —
        /// human 11:36/12:02 moved the settle into the fall with velocity preservation; position is now driven by
        /// a critically-damped approach whose small natural convergence residual carries the organic feel, so the
        /// proportional-gain model is superseded. Kept pending balancer re-confirmation.</summary>
        public float SettleGain { get; set; } = 0.75f;

        /// <summary>Safety clamp on the proportional residual (game-balancer 13 §2.4). CURRENTLY UNUSED for the
        /// same reason as <see cref="SettleGain"/> — kept pending balancer re-confirmation.</summary>
        public float SettleMaxResidualFactor { get; set; } = 0.6f;

        // ── Balance-mode: synchronous group release (TASK-023). Only PhysicsMode.Balance touches these. On a free
        //    beam the ONLY way to keep it level is to load both ends in the SAME physics step, so taps on separate
        //    Idle pieces are buffered into a coalescence window and flushed as one synchronous release group. ──

        /// <summary>Coalescence window (in fixed steps) opened by the first balance-mode tap: further taps on OTHER
        /// Idle pieces within the window join the same release group, and the whole group is released in ONE flush
        /// step so a symmetric pair enters the physics synchronously (net torque ≈ 0 → the free beam stays level).
        /// A single tap that lets the window expire releases one piece → the beam honestly tips → Lost (the
        /// winnability gate is the physics, not an artificial check). ~10 steps ≈ 167 ms @ 1/60 (a comfortable
        /// double-tap; designer W≈120–200 ms). UNVERIFIED — ⟨balancer⟩ tunes against level-sim (no "swallowed
        /// tap" too low, no lag too high). Assembly levels never open the window (one tap = immediate release).</summary>
        public int CoalesceWindowSteps { get; set; } = 10;

        /// <summary>Max pieces in one synchronous release group (v1 = a pair). When this many distinct Idle pieces
        /// have been tapped inside the window the group flushes IMMEDIATELY (minimal latency) instead of waiting
        /// for the window to expire. Generalises cheaply to larger groups later; v1 is capped at 2.</summary>
        public int MaxCoalesceGroup { get; set; } = 2;

        // ── Balance-mode knob (TASK-021). PinHinge-only: retained for the forward-vision catapult levels; the
        //    free-beam seesaws (TASK-023) no longer pin the beam, so these three levers are unused by them. ──

        /// <summary>PinHinge-only (TASK-021), UNUSED by the TASK-023 free-beam seesaws. Angular damping applied to a
        /// pivot beam when it is world-pinned by the balance hinge — drives
        /// the crank to ONE rest inside the sim step budget (no metastable knife-edge, no timeout) yet leaves a
        /// real overload visibly tipping the beam. Raised 2→4 (game-balancer 03 §2.3): with the pin now above the
        /// loaded CM (self-levelling pendulum, C2) the beam oscillates on the way to rest, so it needs firmer
        /// damping to converge deterministically. UNVERIFIED — balancer tunes against level-sim (identical
        /// poseHash over repeats) for balance_scale/seesaw_tip/grand_seesaw; sweep 3–8. The global rest gate before
        /// the balance verdict reuses <see cref="RestSpeedEpsilon"/>/<see cref="RestDurationSeconds"/>.</summary>
        public float BalanceAngularDamping { get; set; } = 4.0f;

        /// <summary>PinHinge-only (TASK-021), UNUSED by the TASK-023 free-beam seesaws. Vertical offset (world units)
        /// that lifts the balance hinge pivot ABOVE the beam's rested centre
        /// so the pinned point sits above the loaded centre of mass — turning the seesaw into a self-levelling
        /// pendulum with a restoring torque toward horizontal (game-balancer 03 §2.1, C2). Pinning at the centre
        /// (offset 0) left the loaded CM above the pivot → inverted pendulum / neutral knife-edge → no restoring
        /// torque → 0 reliable. Applied in <see cref="Gameplay.Core.Mechanic.ShapeFillMechanicController"/> when it
        /// hinges the beam; the adapter's InverseTransformPoint resolves the raised anchor with no positional jerk.
        /// Starting value, UNVERIFIED — balancer sweep 0.45–0.75 (margin over CM: +0.44/+0.32/+0.22 for the 3 seesaws).</summary>
        public float BalancePivotAnchorOffsetY { get; set; } = 0.6f;

        /// <summary>PinHinge-only (TASK-021), UNUSED by the TASK-023 free-beam seesaws. Angular limit (degrees, ±)
        /// applied to the balance hinge as physical end-stops so a single load
        /// on the free-swinging beam cannot tilt it past the ~31° slip angle (friction 0.6) and slide off before its
        /// counter-load arrives (game-balancer 03 §2.2, C3). Horizontal (0°) stays inside the range so the win pose
        /// is reachable. <c>≤0</c> ⇒ limits OFF — kept generic for the forward-vision catapult levels where the beam
        /// must swing free. Starting value, UNVERIFIED — balancer sweep 10–18°.</summary>
        public float BalanceHingeLimitDeg { get; set; } = 14f;
    }
}
