namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// shape_fill physics contract, extending the generic seam with body operations in DOMAIN types
    /// (Vec2/float/int-id) — UnityEngine never leaks into the domain (RFC §3). Unity impl owns a
    /// Physics2D world in Script simulation mode; headless impl replays a pose canvas.
    /// </summary>
    public interface IShapeFillSimulation : IGameplaySimulation
    {
        /// <summary>
        /// Spawn an Idle piece: floating (gravity off) AND non-colliding (collider disabled) at the given
        /// pose, so pre-placed pieces hang without blocking each other's drop column (RFC §5.3). Returns its
        /// stable body id.
        /// </summary>
        int SpawnFloating(PieceType type, Vec2 spawn, float rotation);

        /// <summary>
        /// Spawn a body already at rest in an exact pose (RFC §8, save rehydration). The body does not
        /// re-simulate — its pose is preserved exactly and it can support other pieces. Returns its body id.
        /// </summary>
        int SpawnSettled(PieceType type, Vec2 pos, float rotation);

        /// <summary>Materialise a floating piece: enable its collider AND gravity → it starts falling.</summary>
        void Release(int bodyId);

        BodyPose GetPose(int bodyId);

        /// <summary>max(|linear|, |angular|·r) in domain units — compared against ε for rest.</summary>
        float GetSpeed(int bodyId);

        bool IsWithinBounds(int bodyId);

        void Despawn(int bodyId);

        /// <summary>Install static geometry, world bounds and gravity for the level.</summary>
        void LoadStatics(Polygon[] staticGeometry, Rect bounds, Vec2 gravity);

        /// <summary>Tear down all bodies and static geometry (level reset).</summary>
        void Reset();
    }
}
