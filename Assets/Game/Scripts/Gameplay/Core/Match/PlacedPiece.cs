namespace Gameplay.Core
{
    /// <summary>A settled piece fed to pose-match evaluation (RFC §4.1). Kind + pose + symmetry order.</summary>
    public readonly struct PlacedPiece
    {
        public readonly string Kind;
        public readonly Vec2 Position;
        public readonly float Rotation;
        public readonly int SymmetryOrder;

        public PlacedPiece(string kind, Vec2 position, float rotation, int symmetryOrder)
        {
            Kind = kind;
            Position = position;
            Rotation = rotation;
            SymmetryOrder = symmetryOrder < 1 ? 1 : symmetryOrder;
        }
    }
}
