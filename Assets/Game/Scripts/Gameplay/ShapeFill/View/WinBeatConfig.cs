using DG.Tweening;

namespace Gameplay.View
{
    /// <summary>
    /// Every timing and size of the win beat «В рамку» (TASK-634: design 01 §3/§10, plan 02 §6). Plain-C# instance
    /// registered by the core installer, like <c>TourConfig</c>: the numbers are levers the owner turns while watching the
    /// beat in Unity, not constants. Times are seconds from <c>Won</c>; the segments overlap on purpose (the flight starts
    /// before the bounce settles) and must keep the order <c>0 &lt; FlightStart ≤ BounceEnd &lt; FlightEnd = ReceiveStart
    /// &lt; ReceiveEnd ≤ BeatEnd</c> — a test holds it.
    /// </summary>
    public sealed class WinBeatConfig
    {
        // ── Bounce: a uniform scale in place — grow → shrink → 1, the pivot never moves ─────────────────────────────
        // (TASK-639: the squash/stretch with a hop of TASK-634 read as a jump; the owner asked for a plain bounce scale.)
        /// <summary>Height of the fixed bounce pivot in the figure's bbox: 0.5 = centre (the figure grows and shrinks
        /// around itself, so at the grow peak its bottom dips onto whatever it stands on and at the shrink peak lifts
        /// off it — by (scale − 1)·height/2); 0 = bottom (the base stays on its line, the figure grows upward).</summary>
        public float PivotHeightFraction = 0.5f;
        public float GrowPeak = 0.12f;
        /// <summary>Uniform scale at the grow peak (1 = none).</summary>
        public float GrowScale = 1.12f;
        public float ShrinkPeak = 0.24f;
        /// <summary>Uniform scale at the shrink peak — a little under 1 is the springy overshoot (1 = no overshoot).</summary>
        public float ShrinkScale = 0.95f;
        public float BounceEnd = 0.35f;

        // ── Flight: the map "field → card" interpolated, piece poses eased from rest to their slots ─────────────────
        public float FlightStart = 0.30f;
        public float FlightEnd = 0.80f;
        /// <summary>Design 01 §3: ease-in-out with the speed in the SECOND half — the figure "drops into" the frame.</summary>
        public Ease FlightEase = Ease.InQuad;
        /// <summary>Arc: the Bézier control point sits this fraction of the camera view WIDTH off the straight line.</summary>
        public float ArcFraction = 0.08f;
        /// <summary>Stretch along the flight, peak at its middle (1 = none).</summary>
        public float FlightStretchMaxY = 1.06f;
        /// <summary>The pieces' shadows lift off over this first fraction of the flight.</summary>
        public float ShadowFadeFraction = 0.3f;
        /// <summary>World units the flying figure sits in FRONT of the card's slots (camera looks along +Z), from the
        /// flight start on, so the landed pieces cover the card's own picture.</summary>
        public float FlightZAhead = 0.05f;

        // ── Receive: the frame punches, the window flashes, sparks fly off the frame's perimeter ───────────────────
        public float ReceiveStart = 0.80f;
        public float ReceiveEnd = 1.05f;
        public float ReceivePunch = 1.07f;
        public float FlashAlpha = 0.6f;
        /// <summary>The flash covers the frame's WINDOW, as a fraction of the frame's outer side. Measured on the wooden
        /// frame sprite (the opening is ≈0.72 of the frame on the A5 frame of TASK-634): the frame's pad fraction (0.78)
        /// put the flash's hard edge onto the inner bevel. Used only when the wooden frame is drawn.</summary>
        public float FlashWindowFraction = 0.71f;
        /// <summary>Share of the receive window the flash takes to reach <see cref="FlashAlpha"/> (then fades).</summary>
        public float FlashAttackFraction = 0.2f;
        public int SparkCount = 12;
        /// <summary>± spread of a spark's direction around the frame's outward normal, degrees.</summary>
        public float SparkSpreadDegrees = 35f;
        /// <summary>How far a spark flies, as a fraction of the frame's side on screen.</summary>
        public float SparkDistance = 0.35f;
        /// <summary>Spark size, canvas units (reference 1080 wide, match-width).</summary>
        public float SparkSize = 32f;
        /// <summary>Code-built fallback spark (no sprite): a streak along its flight, this long × this thin (× size).</summary>
        public float FallbackSparkLength = 1.8f;
        public float FallbackSparkThickness = 0.4f;

        // ── End and skip ──────────────────────────────────────────────────────────────────────────────────────────
        public float BeatEnd = 1.30f;
        /// <summary>A tap during the beat jumps to the landing and plays the rest (receive + settle) in this many seconds.</summary>
        public float SkipTailSeconds = 0.15f;
    }
}
