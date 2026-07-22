namespace Gameplay.View
{
    /// <summary>
    /// Presentation-only tuning for the target preview (RFC §4.3): a scaled "picture-on-the-box" overlay
    /// anchored to a camera viewport point, rendered from the target slots in real piece shapes+colours.
    /// Plain view-layer class (not domain — the domain stays Unity-free / headless-simulatable). Numeric
    /// values are <c>⟨balancer/designer⟩</c> starting values (UNVERIFIED); reference screen is portrait
    /// 1080×1920 (human, gate step 3), but the viewport anchor is aspect-independent by construction.
    /// </summary>
    public sealed class TargetPreviewConfig
    {
        /// <summary>Uniform scale of the preview figure (≈0.5 = "half size", ticket request).</summary>
        public float Scale = 0.5f;

        /// <summary>Horizontal viewport anchor of the preview frame (0..1, start: centre).</summary>
        public float AnchorViewportX = 0.5f;

        /// <summary>Vertical viewport anchor of the preview frame (0..1, start: near top).</summary>
        public float AnchorViewportY = 0.9f;

        /// <summary>Distance passed to <c>Camera.ViewportToWorldPoint</c> (ortho: any value &gt; near).</summary>
        public float CameraDistance = 10f;

        /// <summary>World z of the overlay: nearer to camera than pieces (z=0) so it draws on top.</summary>
        public float ZPlane = -0.5f;

        /// <summary>
        /// Assist lever (RFC §4.6, GAME_RULES §7): full-size per-slot on-field ghost. Default OFF —
        /// the mini preview is the primary reference; the ghost is an optional FTUE/A/B aid.
        /// </summary>
        public bool ShowOnFieldGhost = false;
    }
}
