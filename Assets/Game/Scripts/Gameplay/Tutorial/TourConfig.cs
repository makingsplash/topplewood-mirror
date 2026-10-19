using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Every timing and size of the tutorial tour (TASK-613: scenario 01 §3/§9, layout spec 02 §3, art 04). Plain-C#
    /// instance registered by the core installer, like <c>PlayScreenConfig</c>: the numbers are levers the owner turns
    /// while watching the tour in Unity, not constants. Sizes are canvas units of the tour overlay (reference 1080 wide,
    /// match-width — plan 03 §3.9), so a fraction of the screen WIDTH holds on every aspect.
    /// </summary>
    public sealed class TourConfig
    {
        // ── Beats ────────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Pause after the field comes to rest before the 2nd and later tap beats show (01 §3 «Такт 2»):
        /// the player notices the previous piece has landed. Held by the VIEW, not the step — input is open from rest.</summary>
        public float BetweenTapsDelay = 0.3f;
        // The TASK-613 "Совпало" beat and its numbers are gone since TASK-634: every win plays the common win beat
        // «В рамку» (its levers live in Gameplay.View.WinBeatConfig).

        // ── Finger tap cycle (01 §3, ≈1.3 s, repeats until the player taps) ────────────────────────────────────────
        public float ApproachSeconds = 0.25f;
        public float PressSeconds = 0.12f;
        public float ContactSeconds = 0.45f;
        public float LiftSeconds = 0.15f;
        public float PauseSeconds = 0.35f;
        /// <summary>Finger scale at the bottom of the press (1.0 → this → 1.0).</summary>
        public float PressScale = 0.88f;
        /// <summary>Approach start, up-right of the tip, as a fraction of the finger size.</summary>
        public float ApproachOffsetFraction = 0.12f;
        /// <summary>Ripple ring over the contact phase: scale 0.3 → 1.0 of its 240 cu slot, alpha 0.8 → 0 (01 §3).</summary>
        public float RippleStartScale = 0.3f;
        public float RippleStartAlpha = 0.8f;
        /// <summary>Contours flash on each contact (01 §3: «вспыхивает один раз на 0.1 с»): width × this, up and back.</summary>
        public float ContactFlashSeconds = 0.1f;
        public float ContactFlashWidthScale = 1.6f;

        // ── Dim (01 §3: in 0.25 s with the beat; out 0.2 s on the tap so the fall stays visible) ───────────────────
        public float DimInSeconds = 0.25f;
        public float DimOutSeconds = 0.2f;
        /// <summary>A miss flashes the cut-out: a white fill of the hole at this alpha, fading over the seconds.</summary>
        public float MissFlashAlpha = 0.35f;
        public float MissFlashSeconds = 0.3f;

        // ── Finger placement (spec 02 §3) ────────────────────────────────────────────────────────────────────────
        /// <summary>Touch point = the piece contour's centroid moved this fraction of the bbox half-size toward the
        /// side AWAY from the hand (the hand hangs down-right, so the tip sits up-left; mirrored with the rig).</summary>
        public float TouchBiasFraction = 0.25f;
        /// <summary>How far the hand reaches from the tip, as a fraction of <see cref="FingerSize"/> (right, down).</summary>
        public float HandReachRight = 0.62f;
        public float HandReachDown = 0.74f;
        /// <summary>The rig mirrors when the hand would cross the safe area's edge minus this (cu).</summary>
        public float MirrorEdgeMargin = 24f;

        // ── Sizes, canvas units ──────────────────────────────────────────────────────────────────────────────────
        /// <summary>Finger slot (02 §3: ≈20 % of the width at 1080 reference).</summary>
        public float FingerSize = 320f;
        public float FieldOutlineWidth = 8f;
        public float CardOutlineWidth = 4f;
        /// <summary>Cut-out padding around the piece and its soft edge (02 §3); shrunk per beat by the gap rule.</summary>
        public float CutoutPad = 28f;
        public float CutoutFeather = 24f;
        public float CardCutoutPad = 12f;
        public float CardCutoutFeather = 16f;
        public float MinCutoutPad = 8f;

        // ── Tones (art 04) ───────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Contour on the field: white (contrast ≈5.8 to the wall). Art 04 replaced the green #39D353.</summary>
        public Color FieldOutlineColor = Color.white;
        /// <summary>Contour on the card: navy #1F2844 (white on the cream canvas reads ≈1.36).</summary>
        public Color CardOutlineColor = new Color32(0x1F, 0x28, 0x44, 0xFF);
        /// <summary>Dim tone = RGB of the <c>scrim</c> token <c>#2A211A</c> (design/tokens.json; duplicated here,
        /// tech-debt TASK-620). Its alpha is the tap beat's below, not the token's 0x99.</summary>
        public Color DimTone = new Color32(0x2A, 0x21, 0x1A, 0xFF);
        public float TapDimAlpha = 0.60f;

        // ── HUD ──────────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>HUD buttons' alpha while the tour holds the focus (02 §3, T3).</summary>
        public float HudFocusedAlpha = 0.35f;

        /// <summary>One full tap cycle (approach → press → contact → lift → pause).</summary>
        public float TapCycleSeconds => ApproachSeconds + PressSeconds + ContactSeconds + LiftSeconds + PauseSeconds;
    }
}
