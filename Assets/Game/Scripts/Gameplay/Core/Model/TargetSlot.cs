namespace Gameplay.Core
{
    /// <summary>
    /// One cell of the target figure (GAME_RULES §1): which piece <see cref="Kind"/> belongs here and its
    /// target pose (position + rotation). Slots of the same Kind are interchangeable during match.
    /// </summary>
    public readonly struct TargetSlot
    {
        public readonly string Kind;
        public readonly Vec2 Position;
        public readonly float Rotation;

        public TargetSlot(string kind, Vec2 position, float rotation)
        {
            Kind = kind;
            Position = position;
            Rotation = rotation;
        }
    }
}
