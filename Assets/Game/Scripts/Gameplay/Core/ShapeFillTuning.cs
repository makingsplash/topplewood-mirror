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

        /// <summary>Minimum authored start-tilt (rad) that makes a piece settle on REAL contact torque instead of
        /// being snap-captured on first contact (TASK-024). On spawn a piece whose start rotation differs from its
        /// nearest same-Kind slot by more than this (symmetry-reduced) is flagged <c>NeedsPhysicalSettle</c>: it
        /// SKIPS the in-fall snap and stays Dynamic, so contact gravity honestly rights it to rest before the snap
        /// crisps it into the slot (the visible turn is physics, the glide only micro-finishes the residual). Kept
        /// above float/settling noise yet well below the authored rotation-pack minimum (~12°): a flat assembly
        /// piece (start ≈ slot angle) stays under it and takes the byte-for-byte in-fall path. Starting value ~0.05
        /// rad ≈ 3°, UNVERIFIED — ⟨balancer⟩ confirms all 19 rotation levels classify as tilted, flat bases do not.</summary>
        public float PhysicalSettleTiltThreshold { get; set; } = 0.05f;

        /// <summary>Support-probe gap (world units) for the snap gate (TASK-010, human 2026-07-23 15:20). Snap
        /// captures a slot only once the piece has support this close beneath its lower edge (floor or an
        /// already-settled body) — i.e. it is about to sit down, not still free-falling. Kept small so a piece
        /// falling through a higher same-Kind slot's capture window (empty space below) keeps falling to its own
        /// lower slot instead of being grabbed mid-air. Physical & level-agnostic: no dependence on neighbouring
        /// slots' Kind/position. Near landing a piece advances well under this per fixed step, so the gate opens
        /// on the contact frame; QA/balancer to re-bless.</summary>
        public float SnapSupportProbeDistance { get; set; } = 0.1f;

        // ── Honesty gate (TASK-042). The snap assist crisps a piece onto its authored slot pose; in a WRONG release
        //    order that ideal pose can cut THROUGH an already-placed piece (candles snapped down into the plate) or
        //    hang ABOVE the piece's real support (snapped up onto an invisible platform). These two clamps keep the
        //    snap — and the freeze-where-it-lies path — physically admissible so the settled figure never
        //    interpenetrates or floats. In a CORRECT order the target poses are the (non-overlapping, supported)
        //    silhouette, so both clamps always pass → the assembly path is byte-for-byte unchanged. ──

        /// <summary>Max penetration (world units) tolerated between a snap-target / rest pose and an already-Placed
        /// piece before the pose is rejected as interpenetration. Above the Box2D rest slop (~0.005) so a clean
        /// canonical stack (touching contacts) is never rejected, yet far below a gross wrong-order overlap
        /// (candles through the plate ≈0.25). ⟨qa/balancer⟩ to re-bless.</summary>
        public float OverlapTolerance { get; set; } = 0.02f;

        /// <summary>Max upward distance (world units) the snap assist may lift a piece onto its slot. A canonical
        /// piece lands at/above its slot and snaps DOWN/laterally onto real support (lift ≤ 0), so this stays tiny;
        /// it blocks the wrong-order case where the snap would MAGNETISE a piece UP off its support into the slot,
        /// leaving it hanging on an invisible support (owner bug 2026-07-26). ⟨qa/balancer⟩ to re-bless.</summary>
        public float SnapMaxLift { get; set; } = 0.05f;

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

        // ── PinHinge levers (TASK-021 → catapult TASK-025 → de-pinned TASK-027). FULLY UNUSED after TASK-027: the
        //    catapult arm was de-pinned to a free Dynamic body on a low fulcrum (owner playtest rejected the pin as a
        //    "helper"), so PinHinge now has NO caller at all and these three levers read by no code path. Retained,
        //    NOT deleted — config back-compat (old JSON may still carry them) + a historical lever set that would be
        //    re-wired verbatim if a pinned mode ever returns. Do NOT delete. Values left at their last-tuned numbers. ──

        /// <summary>PinHinge-only — UNUSED after TASK-027 (catapult de-pinned to a free-beam ground-stop; PinHinge has
        /// no caller). Angular damping formerly applied to the pinned catapult arm to drive the crank to ONE rest
        /// inside the sim step budget without bleeding off the launch impulse. Retained: re-wired verbatim if a pinned
        /// mode ever returns. Do NOT delete. (History: 4.0→1.2 in game-balancer 03 §5 for the offset-0 limit-free pin,
        /// then 1.2→0.8 in game-balancer 05 §7 to fight launch undershoot.)</summary>
        public float BalanceAngularDamping { get; set; } = 0.8f;

        /// <summary>PinHinge-only — UNUSED after TASK-027 (catapult de-pinned; PinHinge has no caller). Was already
        /// UNUSED by the accepted TASK-025 path (the catapult pinned at the arm's OWN centre, offset 0). Retained as
        /// the historical self-levelling lever (lifted the hinge above the loaded CM for a seesaw pendulum). Do NOT
        /// delete — re-wired verbatim if a pinned mode ever returns.</summary>
        public float BalancePivotAnchorOffsetY { get; set; } = 0.6f;

        /// <summary>PinHinge-only — UNUSED after TASK-027 (catapult de-pinned; PinHinge has no caller). Angular limit
        /// (degrees, ±) formerly applied to the hinge as physical end-stops (<c>≤0</c> ⇒ limits OFF). Set to 0 for the
        /// catapult (the arm had to swing free for the launch flip); now that the arm is a free body its tilt is
        /// limited by the natural ground-stop, not a hinge limit. Retained: re-wired verbatim if a pinned mode ever
        /// returns. Do NOT delete.</summary>
        public float BalanceHingeLimitDeg { get; set; } = 0f;
    }
}
