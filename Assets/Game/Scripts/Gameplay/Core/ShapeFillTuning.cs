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
    }
}
