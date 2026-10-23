using System;
using DG.Tweening;
using UnityEngine;

namespace Ui.Components.Reveal
{
    public enum RevealKind
    {
        /// <summary>From an offset state INTO the prefab values: scale ×<see cref="RevealStep.Scale"/>, position
        /// +<see cref="RevealStep.Offset"/>, alpha ×<see cref="RevealStep.Alpha"/> → prefab.</summary>
        In,
        /// <summary>The first integer in the node's text counts from <see cref="RevealStep.CountFrom"/> up to the
        /// value the controller wrote there (prefix/suffix kept).</summary>
        Count,
        /// <summary>Scale prefab → ×<see cref="RevealStep.Scale"/> → prefab, half the duration each way.</summary>
        Punch,
        /// <summary>Idle: scale/alpha prefab ↔ ×value, Yoyo forever; <see cref="RevealStep.DurationMs"/> is the FULL
        /// cycle. Does not count towards the intro (tap-to-complete does not fast-forward it).</summary>
        Loop,
    }

    /// <summary>
    /// One timed tween of one node in a screen-reveal timeline (TASK-425). Pure data: authored in the screen JSON
    /// (`behaviour: Reveal`, compiler handler <c>RevealHandler</c>) and baked into <see cref="RevealTrack"/> on the
    /// prefab — never hand-edited on the prefab, ui-build rewrites it.
    /// </summary>
    [Serializable]
    public sealed class RevealStep
    {
        public RevealKind Kind;
        /// <summary>Start, ms from the reveal's t = 0 (before skip-shifts).</summary>
        public int AtMs;
        public int DurationMs;

        public bool HasScale;
        public float Scale = 1f;
        public bool HasAlpha;
        public float Alpha = 1f;
        public bool HasOffset;
        public Vector2 Offset;
        /// <summary>In: alpha finishes earlier than the motion when &gt; 0 (a pop that is opaque before it lands).</summary>
        public int AlphaDurationMs;

        public Ease Ease = Ease.OutQuad;
        public float Overshoot = 1.70158f;
        public float CountFrom;

        /// <summary>TASK-667: a sound played when this step starts (a SoundEffect asset name); empty = silent.</summary>
        public string SoundId;
        /// <summary>Pitch shift of that sound in semitones — one clip, a ladder by data (win stars 0 / 2 / 4).</summary>
        public float SoundSemitones;

        public int EndMs => AtMs + DurationMs;
    }
}
