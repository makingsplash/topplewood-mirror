namespace Gameplay.Core
{
    /// <summary>
    /// TASK-097: a finite directed air-flow zone (propeller/fan). Applies a mass-independent ACCELERATION
    /// (<see cref="Direction"/>·<see cref="Acceleration"/>) to every RELEASED Dynamic body whose CENTRE is
    /// inside <see cref="Region"/>, every physics step (see UnityShapeFillSimulation.ApplyFlow). An environment
    /// entity — no state, no tap, not a move; it exists from t=0 and is always on (owner directive 2026-08-05).
    /// Unity-free: only domain types. <see cref="Color"/> is an authoring/render attribute (§D5: the visible mesh
    /// must coincide with <see cref="Region"/>); the physics adapter ignores it. Direction is stored as a unit
    /// vector — the DTO normalises it on the way in (LevelDataDto.AirZoneDto.ToDomain).
    /// </summary>
    public readonly struct AirZone
    {
        /// <summary>Finite force area in world space. MUST equal the rendered mesh (§D5 — visible == force).</summary>
        public readonly Rect Region;

        /// <summary>Unit flow direction (normalised at DTO→domain). Multiplied by <see cref="Acceleration"/>.</summary>
        public readonly Vec2 Direction;

        /// <summary>Magnitude in units/s^2, mass-independent (balancer: default 3.0, authoring band [2,4]).</summary>
        public readonly float Acceleration;

        /// <summary>Optional "#RRGGBB" fill for the visible zone mesh; null/empty → the view's default. View-only.</summary>
        public readonly string Color;

        public AirZone(Rect region, Vec2 direction, float acceleration, string color)
        {
            Region = region;
            Direction = direction;
            Acceleration = acceleration;
            Color = color;
        }
    }
}
