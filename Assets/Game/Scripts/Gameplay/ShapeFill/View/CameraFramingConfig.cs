namespace Gameplay.View
{
    /// <summary>
    /// Presentation-only tuning for content-driven camera framing (TASK-009, RFC 07 §1.1). Plain view-layer
    /// class (not domain — the domain stays Unity-free / headless-simulatable). Numeric values are the
    /// balancer's starting numbers (comment 08:17, UNVERIFIED — tuned after playtest).
    /// </summary>
    public sealed class CameraFramingConfig
    {
        /// <summary>Extra world-space padding added around the content AABB so pieces are not flush to the edge.</summary>
        public float Margin = 0.6f;

        /// <summary>Lower clamp on orthographic size so small levels (e.g. L1) do not zoom in absurdly.</summary>
        public float MinOrthoSize = 3.5f;
    }
}
