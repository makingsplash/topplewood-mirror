using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Seeded bot policy (RFC §4): chooses which pre-placed Idle piece(s) to release next by filling
    /// <paramref name="taps"/> with the world point(s) to tap. The driver releases the WHOLE group in one flush
    /// (TASK-023 §5.3) — a single-element group is the ordinary "one piece at a time" case; a multi-element
    /// group (a symmetric balance pair) is released synchronously in the same physics step. For a rest-gated policy
    /// (<see cref="GatedByRest"/> == true) the driver calls it ONLY when
    /// <see cref="Gameplay.Core.Mechanic.ShapeFillMechanicController.ReadyForNextRelease"/> is true, so its protocol
    /// stays "release group → wait for rest → release next" — the patient game every prover currently explores.
    /// A non-gated policy (TASK-060: <see cref="ScriptedReleasePolicy"/>) is asked on EVERY Running step and drives
    /// its own timing, expressing the "fast" game (tap the next piece mid-flight) the player has always been able to
    /// play. Returns false (and leaves <paramref name="taps"/> empty) when nothing is released this step.
    /// </summary>
    public interface ISimBotPolicy
    {
        bool TryNextRelease(GameModel model, List<Vec2> taps);

        /// <summary>
        /// True → the driver releases this policy's next group only once the field is at rest
        /// (<c>ReadyForNextRelease</c>), the patient protocol. False → the driver asks the policy every Running
        /// step and the policy times its own releases (TASK-060). "Patience" is a property of the PLAYER, not the
        /// field, so the policy — not the driver — owns the decision. An explicit member (not a default-interface
        /// member) so the C# language version Unity compiles with is irrelevant.
        /// </summary>
        bool GatedByRest { get; }
    }
}
