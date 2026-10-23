namespace Gameplay.Core.Adapters
{
    /// <summary>
    /// Receiver of piece impacts reported by the physics adapter (TASK-667 plan §2.1). The adapter attaches a
    /// reporter to every spawned body ONLY when a sink is supplied: prod passes the sound presenter, the sim harness
    /// passes a counting sink. Contract: <see cref="Report"/> runs INSIDE the physics step (a Box2D contact callback),
    /// so a sink may only record — it MUST NOT touch the simulation, the model or any rigidbody.
    /// </summary>
    public interface IPieceImpactSink
    {
        void Report(PieceImpact impact);
    }

    /// <summary>One new contact of a piece: the reporting body and the approach speed along the contact normal
    /// (units/s) — the same quantity Box2D v3 carries as <c>approachSpeed</c> in its hit events. Unity-free.</summary>
    public readonly struct PieceImpact
    {
        public readonly int BodyId;
        public readonly float Speed;

        public PieceImpact(int bodyId, float speed)
        {
            BodyId = bodyId;
            Speed = speed;
        }
    }
}
