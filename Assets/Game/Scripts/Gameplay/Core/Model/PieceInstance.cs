namespace Gameplay.Core
{
    /// <summary>
    /// Runtime instance of a pre-placed piece (RFC §2.4). <see cref="InstanceId"/> is deterministic
    /// (<c>seed*1000 + placementIndex</c>) so save/load re-associates bodies correctly. Pose is mirrored
    /// FROM the simulation each tick — the domain never computes physics. Replaces the removed BlockInstance.
    /// </summary>
    public sealed class PieceInstance
    {
        public int InstanceId { get; }
        public int PlacementIndex { get; }
        public string Kind { get; }

        public PieceLifecycle State { get; set; }
        public Vec2 Position { get; set; }
        public float Rotation { get; set; }

        /// <summary>Seconds the body has continuously measured "at rest" (below ε). Transient, not saved.</summary>
        public float RestTimer { get; set; }

        public PieceInstance(int instanceId, int placementIndex, string kind, Vec2 startPosition, float startRotation)
        {
            InstanceId = instanceId;
            PlacementIndex = placementIndex;
            Kind = kind;
            State = PieceLifecycle.Idle;
            Position = startPosition;
            Rotation = startRotation;
        }
    }
}
