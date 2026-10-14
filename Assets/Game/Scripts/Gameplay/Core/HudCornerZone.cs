using System;

namespace Gameplay.Core
{
    /// <summary>
    /// A HUD button parked in a BOTTOM corner of the screen, described the way its canvas lays it out (TASK-539):
    /// offsets and size in canvas units of a <c>CanvasScaler.ScaleWithScreenSize</c> canvas, measured from the SAFE
    /// side and bottom edges. Unity-free, so the camera framing (<see cref="CameraFramingMath.TryFrame"/>) — the live
    /// presenter, the headless validator and the editor capture alike — can keep the level's figure and pieces out of
    /// the corner the button owns, and the UI that builds the button reads its layout from the SAME value: the
    /// reserve cannot drift from where the button actually is.
    ///
    /// The reserved zone is the CORNER QUADRANT from the button's inner edges to the screen edges, not the button
    /// rect alone: content under or beside the button is just as covered for a thumb, and a quadrant makes the
    /// clearance monotone in the framing (zooming out or raising the content only ever frees it).
    /// </summary>
    public readonly struct HudCornerZone
    {
        /// <summary>Bottom-right (true) or bottom-left (false).</summary>
        public readonly bool Right;
        /// <summary>Canvas units from the safe side edge to the button's outer edge.</summary>
        public readonly float SideInset;
        /// <summary>Canvas units from the safe bottom edge to the button's bottom.</summary>
        public readonly float BottomInset;
        public readonly float Width;
        public readonly float Height;
        /// <summary>Canvas units around the button that also belong to it (glow, badge overhang).</summary>
        public readonly float Pad;
        public readonly float RefWidth;
        public readonly float RefHeight;
        /// <summary><c>CanvasScaler.matchWidthOrHeight</c>, 0 = width … 1 = height.</summary>
        public readonly float Match;

        public HudCornerZone(bool right, float sideInset, float bottomInset, float width, float height, float pad,
            float refWidth, float refHeight, float match)
        {
            Right = right;
            SideInset = sideInset;
            BottomInset = bottomInset;
            Width = width;
            Height = height;
            Pad = pad;
            RefWidth = refWidth;
            RefHeight = refHeight;
            Match = match;
        }

        /// <summary>
        /// The zone in viewport fractions for a screen of <paramref name="aspect"/> (width / height) whose safe area
        /// is inset by <paramref name="safeSide"/> (fraction of width, on the zone's side) and
        /// <paramref name="safeBottom"/> (fraction of height). <paramref name="sideFraction"/> is how far the zone
        /// reaches in from its side edge, <paramref name="topFraction"/> how high it reaches from the bottom.
        /// The canvas scale of ScaleWithScreenSize is (w/rw)^(1−m)·(h/rh)^m, so a canvas unit is a fixed fraction of
        /// the screen for a given aspect — the zone does not depend on the pixel count.
        /// </summary>
        public void ToViewport(float aspect, float safeSide, float safeBottom,
            out float sideFraction, out float topFraction)
        {
            var a = aspect <= 0f ? 1f : aspect;
            var perWidth = MathF.Pow(a, -Match) / (MathF.Pow(RefWidth, 1f - Match) * MathF.Pow(RefHeight, Match));
            var perHeight = perWidth * a;
            sideFraction = safeSide + (SideInset + Width + Pad) * perWidth;
            topFraction = safeBottom + (BottomInset + Height + Pad) * perHeight;
        }

        /// <summary>The hint booster button (TASK-237), bottom-right. <c>HintBoosterPresenter</c> builds the button
        /// from these numbers. Pad 25 = the proactive glow (200 around a 150 button) and covers the badge's 12-unit
        /// overhang.</summary>
        public static readonly HudCornerZone HintButton = new(
            right: true, sideInset: 40f, bottomInset: 200f, width: 150f, height: 150f, pad: 25f,
            refWidth: 1080f, refHeight: 1920f, match: 0.5f);
    }
}
