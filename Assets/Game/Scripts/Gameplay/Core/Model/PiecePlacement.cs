namespace Gameplay.Core
{
    /// <summary>
    /// A piece pre-placed on the field at level start (GAME_RULES §1): its <see cref="Type"/> and its
    /// starting pose (usually ABOVE its target slot, since there is no aiming — a straight drop lands it on
    /// place; the puzzle is the release ORDER). Replaces the removed BlockPool feed.
    /// </summary>
    public readonly struct PiecePlacement
    {
        public readonly PieceType Type;
        public readonly Vec2 StartPosition;
        public readonly float StartRotation;

        /// <summary>TASK-031b: when true, this piece is spawned Dynamic ON the field at t=0 (a lever/obstacle that
        /// is part of the physics from the start) instead of hanging Idle to be tapped. It is never tap-releasable
        /// and never counts as a player attempt; the release ORDER puzzle is over the remaining Idle pieces.</summary>
        public readonly bool Preplaced;

        /// <summary>TASK-054: when true, this piece is a TOOL (lever/projectile/knock-away), not a part of the target
        /// figure — losing it past the field bounds is fine and does NOT fail the level. Absent → false → ESSENTIAL:
        /// the default matches the pre-TASK-054 rule "any lost piece blocks the win". Losing an essential piece is an
        /// immediate loss (GAME_RULES §2). Property of the PLACEMENT, not the <see cref="PieceType"/>: two pieces of
        /// the same kind may differ (one figure part, one tool) — the same identity argument as <see cref="Preplaced"/>.</summary>
        public readonly bool Expendable;

        public PiecePlacement(PieceType type, Vec2 startPosition, float startRotation = 0f, bool preplaced = false,
            bool expendable = false)
        {
            Type = type;
            StartPosition = startPosition;
            StartRotation = startRotation;
            Preplaced = preplaced;
            Expendable = expendable;
        }
    }
}
