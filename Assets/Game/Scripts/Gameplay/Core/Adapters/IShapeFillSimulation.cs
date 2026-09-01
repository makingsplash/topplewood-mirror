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

        /// <summary>Materialise a floating piece: enable its collider AND gravity → it starts falling.</summary>
        void Release(int bodyId);

        // TASK-305 (freeze removal, owner variant A): the FreezeAt seam is GONE. No path ever locks a body kinematic
        // anymore — a rested piece is marked Placed but stays a live Dynamic body, and the verdict is judged at
        // global rest of the whole field. Deleting the seam is the proof that zero pieces become kinematic.

        BodyPose GetPose(int bodyId);

        /// <summary>max(|linear|, |angular|·r) in domain units — compared against ε for rest.</summary>
        float GetSpeed(int bodyId);

        /// <summary>
        /// True when the body has physical support directly beneath it — the floor (static geometry) or an
        /// already-settled (kinematic) body within <paramref name="maxGap"/> below its lower edge — i.e. it is
        /// about to come to rest on something rather than still in free fall. The snap-to-slot assist is gated
        /// on this so a piece is captured only while it is settling, never plucked out of the air by a slot it
        /// merely falls THROUGH on the way to a lower one (TASK-010 root cause). A still-falling dynamic body
        /// beneath does NOT count as support.
        /// </summary>
        bool HasSupportBelow(int bodyId, float maxGap);

        /// <summary>
        /// Linear velocity (domain units/s) of a body. Read at capture to seed the soft-settle glide so it
        /// continues the fall's momentum instead of restarting from a dead stop (human 12:02 — no hitch).
        /// </summary>
        Vec2 GetVelocity(int bodyId);

        bool IsWithinBounds(int bodyId);

        void Despawn(int bodyId);

        /// <summary>Install static geometry, world bounds and gravity for the level.</summary>
        void LoadStatics(Polygon[] staticGeometry, Rect bounds, Vec2 gravity);

        /// <summary>
        /// Install directed air-flow zones for the level (TASK-097). Null/empty ⇒ no flow (back-compat: every
        /// pre-TASK-097 level parses to null and is byte-for-byte unchanged). Each RELEASED Dynamic body whose
        /// centre is inside a zone receives a mass-independent ACCELERATION every Step (RFC §2.2). The headless
        /// pose-canvas simulation replays no physics, so it treats this as a no-op (like its LoadStatics).
        /// </summary>
        void LoadFlowZones(AirZone[] zones);

        /// <summary>Tear down all bodies and static geometry (level reset).</summary>
        void Reset();
    }
}
