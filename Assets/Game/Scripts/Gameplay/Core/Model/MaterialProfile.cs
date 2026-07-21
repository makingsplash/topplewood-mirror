namespace Gameplay.Core
{
    /// <summary>
    /// Physics material profile for a block. Concrete numbers are <c>⟨balancer⟩</c>; mass/weight are
    /// derived from shape + density by the physics adapter, never set directly (GAME_RULES §1).
    /// </summary>
    public readonly struct MaterialProfile
    {
        public readonly float Density;
        public readonly float Friction;
        public readonly float Restitution;

        public MaterialProfile(float density, float friction, float restitution)
        {
            Density = density;
            Friction = friction;
            Restitution = restitution;
        }
    }
}
