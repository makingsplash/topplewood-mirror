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

        /// <summary>
        /// Lock a settled body at an exact pose: kinematic, zero velocity — stops post-rest drift and turns it
        /// into a stable support (mirrors SpawnSettled). Called at Falling→Placed (RFC 07 §2.1).
        /// </summary>
        void FreezeAt(int bodyId, Vec2 position, float rotation);

        /// <summary>
        /// RETAINED, NO CALLER after TASK-027: TASK-025 used this to pin the catapult arm, but the owner playtest
        /// rejected the pin as a "helper" and TASK-027 de-pinned the arm to a free Dynamic body on a low fulcrum
        /// (natural ground-stop). Kept in the interface + both impls (retained-infra) so a pinned mode can be re-wired
        /// without touching the seam. Pin a body to the world with a revolute hinge at
        /// <paramref name="anchor"/> (a world point). Unlike <see cref="FreezeAt"/> the body stays Dynamic so weight
        /// on one side cranks it around the anchor; a balance angular damper drives it to a single deterministic
        /// rest. Velocity is NOT zeroed (the contact impulse carries through). Pass the beam's own centre as the
        /// anchor so an unloaded beam balances (an off-centre pin would sit under a constant gravity torque). The
        /// headless pose canvas has no physics, so it is a no-op there.
        /// </summary>
        void PinHinge(int bodyId, Vec2 anchor);

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

        /// <summary>Tear down all bodies and static geometry (level reset).</summary>
        void Reset();
    }
}
