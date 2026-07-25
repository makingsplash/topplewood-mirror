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

        public PiecePlacement(PieceType type, Vec2 startPosition, float startRotation = 0f, bool preplaced = false)
        {
            Type = type;
            StartPosition = startPosition;
            StartRotation = startRotation;
            Preplaced = preplaced;
        }
    }
}
