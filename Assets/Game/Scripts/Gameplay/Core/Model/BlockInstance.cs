namespace Gameplay.Core
{
    /// <summary>
    /// Runtime instance of a pool block. <see cref="InstanceId"/> is deterministic (sequential from
    /// pool order) so save/load re-associates bodies correctly (RFC §6). Pose is mirrored FROM the
    /// simulation each tick — the domain never computes physics.
    /// </summary>
    public sealed class BlockInstance
    {
        public int InstanceId { get; }
        public int PoolIndex { get; }
        public string Kind { get; }

        public BlockLifecycle State { get; set; }
        public Vec2 Position { get; set; }
        public float Rotation { get; set; }

        /// <summary>Seconds the body has continuously measured "at rest" (below ε). Transient, not saved.</summary>
        public float RestTimer { get; set; }

        public BlockInstance(int instanceId, int poolIndex, string kind)
        {
            InstanceId = instanceId;
            PoolIndex = poolIndex;
            Kind = kind;
            State = BlockLifecycle.Floating;
        }
    }
}
