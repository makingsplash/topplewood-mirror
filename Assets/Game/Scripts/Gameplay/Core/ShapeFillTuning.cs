namespace Gameplay.Core
{
    /// <summary>
    /// Runtime tuning knobs for the shape_fill mechanic (config-over-hardcode, RFC §10). All values are
    /// the balancer's starting numbers, UNVERIFIED — tuned after playtests (θ lives per-level in LevelData).
    /// FixedStep is a start constant; determinism/reproducibility of the physics path is deferred (ADR-0001/M0).
    ///
    /// TASK-043 (assist-layer removal): the snap-to-slot, soft-settle glide, physical-settle classification,
    /// TASK-042 honesty gate and retained PinHinge levers are all gone — a released piece falls, comes to rest
    /// under honest physics and is frozen where it lies. What remains are the rest-detection, release-damping and
    /// balance coalescence knobs the honest-physics loop still needs.
    /// </summary>
    public sealed class ShapeFillTuning
    {
        /// <summary>ε — rest speed threshold, domain units/s (max(|lin|,|ang|·r)).</summary>
        public float RestSpeedEpsilon { get; set; } = 0.05f;

        /// <summary>T — how long a body must stay below ε before it counts as Placed.</summary>
        public float RestDurationSeconds { get; set; } = 0.5f;

        /// <summary>Fixed simulation step (s). Acceleration is by step COUNT, never by enlarging this.</summary>
        public float FixedStep { get; set; } = 1f / 60f;

        /// <summary>Linear damping applied on Release — light, does not stall the drop.</summary>
        public float ReleaseLinearDrag { get; set; } = 0.5f;

        /// <summary>Angular damping applied on Release — the main lever: kills half-disc rolling so rest is
        /// reached fast (secondary to the mandatory Freeze-on-Placed).</summary>
        public float ReleaseAngularDrag { get; set; } = 3.0f;

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
    }
}
