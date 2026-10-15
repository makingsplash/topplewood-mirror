using System;

namespace Gameplay.Core
{
    /// <summary>
    /// A HUD button parked in a corner of the screen, described the way its canvas lays it out (TASK-539; top corners
    /// TASK-562/563): offsets and size in canvas units of a <c>CanvasScaler.ScaleWithScreenSize</c> canvas, measured
    /// from the SAFE side edge and the SAFE bottom (or, for <see cref="Top"/> zones, top) edge. Unity-free, so the
    /// camera framing (<see cref="CameraFramingMath.TryFrame"/>) — the live presenter, the headless validator and the
    /// editor capture alike — can keep the level's figure and pieces out of the corner the button owns, the target
    /// preview can keep its card out of the top corners, and the UI that builds the button reads its layout from the
    /// SAME value: the reserve cannot drift from where the button actually is.
    ///
    /// The reserved zone is the CORNER QUADRANT from the button's inner edges to the screen edges, not the button
    /// rect alone: content under or beside the button is just as covered for a thumb, and a quadrant makes the
    /// clearance monotone in the framing (zooming out or moving the content toward the centre only ever frees it).
    /// </summary>
    public readonly struct HudCornerZone
    {
        /// <summary>Right (true) or left (false) corner.</summary>
        public readonly bool Right;
        /// <summary>Top (true) or bottom (false) corner.</summary>
        public readonly bool Top;
        /// <summary>Canvas units from the safe side edge to the button's outer edge.</summary>
        public readonly float SideInset;
        /// <summary>Canvas units from the safe bottom edge (top edge for a <see cref="Top"/> zone) to the button.</summary>
        public readonly float EdgeInset;
        public readonly float Width;
        public readonly float Height;
        /// <summary>Canvas units around the button that also belong to it (glow, badge overhang).</summary>
        public readonly float Pad;
        public readonly float RefWidth;
        public readonly float RefHeight;
        /// <summary><c>CanvasScaler.matchWidthOrHeight</c>, 0 = width … 1 = height.</summary>
        public readonly float Match;

        public HudCornerZone(bool right, bool top, float sideInset, float edgeInset, float width, float height,
            float pad, float refWidth, float refHeight, float match)
        {
            Right = right;
            Top = top;
            SideInset = sideInset;
            EdgeInset = edgeInset;
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
        /// <paramref name="safeEdge"/> (fraction of height, on the zone's top or bottom edge).
        /// <paramref name="sideFraction"/> is how far the zone reaches in from its side edge,
        /// <paramref name="depthFraction"/> how far it reaches in from its top/bottom edge.
        /// The canvas scale of ScaleWithScreenSize is (w/rw)^(1−m)·(h/rh)^m, so a canvas unit is a fixed fraction of
        /// the screen for a given aspect — the zone does not depend on the pixel count.
        /// </summary>
        public void ToViewport(float aspect, float safeSide, float safeEdge,
            out float sideFraction, out float depthFraction)
        {
            var a = aspect <= 0f ? 1f : aspect;
            var perWidth = MathF.Pow(a, -Match) / (MathF.Pow(RefWidth, 1f - Match) * MathF.Pow(RefHeight, Match));
            var perHeight = perWidth * a;
            sideFraction = safeSide + (SideInset + Width + Pad) * perWidth;
            depthFraction = safeEdge + (EdgeInset + Height + Pad) * perHeight;
        }

        /// <summary>The hint booster button (TASK-237), bottom-right. <c>HintBoosterPresenter</c> builds the button
        /// from these numbers. Pad 25 = the proactive glow (200 around a 150 button) and covers the badge's 12-unit
        /// overhang.</summary>
        public static readonly HudCornerZone HintButton = new(
            right: true, top: false, sideInset: 40f, edgeInset: 200f, width: 150f, height: 150f, pad: 25f,
            refWidth: 1080f, refHeight: 1920f, match: 0.5f);

        /// <summary>RESTART (TASK-562, variant B), top-right. <c>RestartButtonPresenter</c> builds it from these
        /// numbers via <c>HudTopButtons</c>. Size and insets are measured on the owner-approved mockup
        /// <c>hudtop-B-prod-01</c>: sticker contour 199 px at 1 cu = 1.104 px, 232/256 of the sprite ⇒ a 200-unit slot,
        /// ≈28 units from the side and ≈30 from the top. No glow or badge ⇒ no pad.</summary>
        public static readonly HudCornerZone RestartButton = new(
            right: true, top: true, sideInset: 28f, edgeInset: 30f, width: 200f, height: 200f, pad: 0f,
            refWidth: 1080f, refHeight: 1920f, match: 0.5f);

        /// <summary>The settings gear (TASK-562, variant B), top-left — the mirror twin of <see cref="RestartButton"/>.</summary>
        public static readonly HudCornerZone SettingsButton = new(
            right: false, top: true, sideInset: 28f, edgeInset: 30f, width: 200f, height: 200f, pad: 0f,
            refWidth: 1080f, refHeight: 1920f, match: 0.5f);

        /// <summary>The two top corners the framing and the preview card keep clear (TASK-563).</summary>
        public static readonly HudCornerZone[] TopButtons = { SettingsButton, RestartButton };
    }
}
