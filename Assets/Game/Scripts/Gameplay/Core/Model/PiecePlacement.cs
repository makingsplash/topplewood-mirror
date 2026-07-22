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

        public PiecePlacement(PieceType type, Vec2 startPosition, float startRotation = 0f)
        {
            Type = type;
            StartPosition = startPosition;
            StartRotation = startRotation;
        }
    }
}
