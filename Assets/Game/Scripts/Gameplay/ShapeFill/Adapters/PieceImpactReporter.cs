using Gameplay.Core.Adapters;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Impact source for the «PieceLand» sound (TASK-667 plan §2.1): a new contact of this body, NOT its transition to
    /// Placed (design TASK-666 §3.2). Strength = the approach speed along the contact normal, the semantics of Box2D v3
    /// hit events (its low-level API is a separate world, so only the meaning is borrowed). The normal component, not
    /// <c>relativeVelocity.magnitude</c>: sliding along the floor is not a knock.
    ///
    /// Read-only by contract: this component only reports — no write to the rigidbody, transform or model. A collision
    /// callback is a report of contacts Box2D builds anyway; having a receiver changes neither the solver nor the step
    /// order, which is why the sim harness can attach it and still match poseHash byte for byte (plan §2.2, §8 T6).
    /// Added by <see cref="UnityShapeFillSimulation"/> only when a sink is supplied.
    /// </summary>
    public sealed class PieceImpactReporter : MonoBehaviour
    {
        private int _bodyId;
        private IPieceImpactSink _sink;

        public void Bind(int bodyId, IPieceImpactSink sink)
        {
            _bodyId = bodyId;
            _sink = sink;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_sink == null || collision.contactCount == 0)
                return;

            // One event per piece↔piece pair: the body with the smaller id reports it. Statics carry no reporter, so a
            // piece hitting the floor or a wall always reports.
            var other = collision.rigidbody;
            if (other != null && other.TryGetComponent<PieceImpactReporter>(out var otherReporter)
                              && otherReporter._bodyId < _bodyId)
                return;

            var contact = collision.GetContact(0); // no array allocation
            var speed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, contact.normal));
            _sink.Report(new PieceImpact(_bodyId, speed));
        }
    }
}
