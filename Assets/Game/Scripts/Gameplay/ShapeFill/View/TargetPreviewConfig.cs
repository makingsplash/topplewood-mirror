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
        /// <summary>
        /// Uniform-scale CEILING of the preview figure (TASK-033): ≈0.5 = "half size" cap. The view auto-fits
        /// the figure into the reserved top band (<see cref="CameraFramingConfig.PreviewBandFraction"/>) and
        /// never scales ABOVE this; tall/wide figures shrink below it so the card lies fully inside the band.
        /// </summary>
        public float Scale = 0.5f;

        /// <summary>Lower clamp on the auto-fit scale (TASK-033) so the preview card never collapses to a dot.</summary>
        public float MinScale = 0.15f;

        /// <summary>Horizontal viewport anchor of the preview frame (0..1, start: centre).</summary>
        public float AnchorViewportX = 0.5f;

        /// <summary>
        /// Vertical viewport anchor of the preview frame (0..1). The view DERIVES the anchor from the reserved
        /// band centre (<c>1 − PreviewBandFraction/2</c>, TASK-033); at band=0.20 that is 0.90, identical to
        /// this legacy default — kept as a fine-tune fallback only.
        /// </summary>
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

        /// <summary>
        /// Show a white square backdrop behind the preview figure (TASK-110), so the silhouette reads
        /// apart from the screen background. Default ON. Lives inside the preview root, so it inherits the
        /// band anchor + auto-fit scale; the auto-fit denominator switches to the square side when enabled.
        /// </summary>
        public bool BackdropEnabled = true;

        /// <summary>
        /// Hex colour of the backdrop (TASK-110), e.g. "#FFFFFF". Parsed via ColorUtility.TryParseHtmlString
        /// (invalid ⇒ white), consistent with how piece/static colours arrive as hex from data. Rendered
        /// OPAQUE through <see cref="BlockMaterial.Shared"/> — the alpha channel is NOT blended
        /// (translucency is a separate translucent-material path, tracked as tech-debt).
        /// </summary>
        public string BackdropColorHex = "#FFFFFF";

        /// <summary>
        /// Padding (in the figure's local units) from the figure bbox to the square edge (TASK-110):
        /// side = max(figureSize.x, figureSize.y) + 2·BackdropPadding. Starting value — UNVERIFIED,
        /// tuned by playtest/balancer alongside <see cref="Scale"/>.
        /// </summary>
        public float BackdropPadding = 0.35f;
    }
}
