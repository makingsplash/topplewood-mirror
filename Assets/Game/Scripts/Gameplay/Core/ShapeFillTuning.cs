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
    /// the co-release coincidence-window knob (TASK-374) the honest-physics loop still needs.
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

        // ── Co-release coincidence window (TASK-374, replaces the TASK-023 balance coalescence helper). A level may
        //    DECLARE groups of pieces (LevelData.CoReleaseGroups) that must be tapped TOGETHER to fall. A single tap
        //    on a group member does NOTHING; it just opens a silent window in which the OTHER members must be tapped.
        //    When every member of a group has been tapped inside the window all its pieces release in ONE step
        //    (synchronous entry into the physics — a symmetric pair keeps the free beam level). An INCOMPLETE group
        //    goes SILENT when the window expires (nothing falls, nothing is shown — no "armed" state, no guaranteed
        //    wait, no single-piece drop). A piece NOT in any group releases immediately on tap, in every mode. ──

        /// <summary>W — coincidence window (in fixed steps) opened by the first tap on a co-release group member: the
        /// remaining members of THAT group must be tapped within W steps or the group silently resets (nothing falls).
        /// This is a CLASS parameter ("how many steps to wait for the rest of a declared group"), not a per-level value.
        /// 4 steps ≈ 67 ms @ 1/60 — centre of the industry "simultaneous" band (~50 ms), comfortable for two thumbs
        /// yet distinctly stricter than the old 167 ms helper (no "I was helped" feel). Owner decision 2026-09-14
        /// (variant B). UNVERIFIED — ⟨balancer⟩ tunes against playtest (too low → swallowed co-taps; too high → drifts
        /// back into a forgiving helper). W=0/1 (one step) is intentionally NOT the default: two human fingers scatter
        /// over several frames, so a literal-zero window is unplayable while the sim (which emits a pair in one step)
        /// would still pass — a green gate over a dead level. A level with no CoReleaseGroups never opens a window.
        /// TASK-482: in the EDITOR with no touchscreen (mouse) <c>EditorMouseCoReleaseWindow</c> overrides this to a
        /// wide window (≈0.6 s) so a pair can be clicked member by member; device builds and the sim keep this value.</summary>
        public int CoReleaseWindowSteps { get; set; } = 4;
    }
}
