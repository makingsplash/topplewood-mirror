namespace Gameplay.Audio
{
    /// <summary>
    /// Knobs of the «PieceLand» impact sound (TASK-667 plan §2.3, design TASK-666 §3.2/§3.4). Data, not constants in
    /// code — the balancer's lever beside <see cref="Gameplay.Core.ShapeFillTuning"/>. The speed numbers come from the
    /// sim harness's impact distribution (the <c>[level-sim] impacts:</c> line, see the as-built of TASK-667), not from
    /// an eye; the volumes and the cooldown are the design's starting values for the owner's ear.
    /// </summary>
    public sealed class ImpactSoundTuning
    {
        /// <summary>Below this approach speed (units/s) an impact is silent: jitter, micro-bounces, a preplaced piece
        /// settling at t = 0. TASK-667 corpus (676k impacts, SIM_IMPACTS=1): p20 0.10 · p50 0.77 · p80 5.19 · p95 8.46 —
        /// two populations, half of all contacts are settling contacts under 0.77, the drops sit at 5–8.5. 1.0 lies above
        /// the settling half; the exact valley needs a histogram, and the ear decides.</summary>
        public float MinSpeed { get; set; } = 1f;

        /// <summary>At and above this approach speed (units/s) an impact plays at <see cref="MaxVolume"/> — p95 of the
        /// same corpus.</summary>
        public float FullSpeed { get; set; } = 8.5f;

        /// <summary>Volume scale of the softest audible impact (design §4: impacts are the quiet layer).</summary>
        public float MinVolume { get; set; } = 0.15f;

        /// <summary>Volume scale of the hardest impact.</summary>
        public float MaxVolume { get; set; } = 0.45f;

        /// <summary>One body is not heard again within this time (s): a bounce is one knock, not a rattle.</summary>
        public float CooldownSeconds { get; set; } = 0.12f;

        /// <summary>At most this many impact voices start in one frame — the strongest ones; the rest are dropped.</summary>
        public int MaxVoicesPerFrame { get; set; } = 3;
    }
}
