namespace Gameplay.Core.Adapters
{
    /// <summary>Position + rotation (radians) of a simulated body, in domain space.</summary>
    public readonly struct BodyPose
    {
        public readonly Vec2 Position;
        public readonly float Rotation;

        public BodyPose(Vec2 position, float rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }
}
