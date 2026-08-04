namespace Gameplay.Core
{
    /// <summary>
    /// Config-over-hardcode (TASK-085) single source of the minimum tap zone (owner directive 2026-08-04): the
    /// screen-space touch target every tapable piece must clear, expressed in dp so it is a PHYSICAL size on the
    /// device rather than a world constant. Read by BOTH the runtime <c>CameraTapTolerance</c> (live dpi/height +
    /// live camera orthoSize → the halo radius the two-tier hit-test falls back to) AND the headless
    /// <see cref="LevelDataValidator"/> (reference device + the framing math predicted per level → the author-time
    /// spacing warning). One knob (<see cref="MinTapDp"/>) drives both so they can never disagree on the target.
    ///
    /// The camera is framed per level (<c>CameraFramingPresenter</c>), so world-per-pixel differs level to level —
    /// a world constant would give a different physical zone on a giraffe vs a locomotive. The dp→world conversion
    /// therefore multiplies by the LIVE (runtime) or PREDICTED (validator) orthoSize; see the formula in
    /// <c>CameraTapTolerance</c> / <see cref="LevelDataValidator"/>.
    /// </summary>
    public sealed class TapZoneConfig
    {
        /// <summary>Minimum tap-target DIAMETER in dp. 48dp = the Android minimum touch target (owner's measure).
        /// Both the runtime halo (radius = ½·this in world units at the live orthoSize) and the author-time spacing
        /// warning (two tapable start poses closer than this in world units at the predicted orthoSize) key on it.
        /// UNVERIFIED — ⟨balancer⟩ may move it after a device playtest (it is a config, not a save field).</summary>
        public float MinTapDp { get; set; } = 48f;

        /// <summary>Phase flag (TASK-085 §7): false = the spacing check is a non-blocking WARNING (this task);
        /// flip to true (phase 2, after the corpus is cleaned) to make it a hard validation ERROR that refuses to
        /// load a too-tight level. Default false so no existing level is broken by shipping the guard.</summary>
        public bool EnforceMinDistanceAsError { get; set; } = false;

        // ── Reference device for the CI/headless predicted zone (owner's measure, 1080×2400 @ 420 dpi). The RUNTIME
        //    adapter never reads these — it uses the LIVE Screen.dpi/Screen.height — they exist only so the validator
        //    can predict a physical zone with no device attached. See the RefKDevice note re: tablet/edge aspects. ──
        public float RefDpi { get; set; } = 420f;
        public int RefPixelHeight { get; set; } = 2400;

        /// <summary>Reference camera aspect (width/height) for the predicted framing — portrait 1080×2400.</summary>
        public float ReferenceAspect { get; set; } = 1080f / 2400f;

        // ── Reference framing constants: MUST mirror CameraFramingConfig defaults (doc-sync, TASK-085 §10). Only the
        //    predicted CI warning threshold depends on them — the runtime tap halo lives on the LIVE orthoSize — but
        //    a drift here would make the author-time warning fire at a different distance than the game actually
        //    frames, so the correspondence is asserted in the doc-sync checklist. ──
        public float RefMargin { get; set; } = 0.6f;
        public float RefPreviewBand { get; set; } = 0.20f;
        public float RefMinOrthoSize { get; set; } = 3.5f;

        /// <summary>Device constant of the dp→world conversion at the reference device: <c>(dpi/160)·2/pixelHeight</c>.
        /// World-per-dp = this · orthoSize, so world tap diameter = MinTapDp · RefKDevice · orthoSize.</summary>
        public float RefKDevice => (RefDpi / 160f) * 2f / RefPixelHeight;
    }
}
