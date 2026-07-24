using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Seeded bot policy (RFC §4): chooses which pre-placed Idle piece(s) to release next by filling
    /// <paramref name="taps"/> with the world point(s) to tap. The driver releases the WHOLE group in one flush
    /// (TASK-023 §5.3) — a single-element group is the ordinary "one piece at a time" case; a multi-element
    /// group (a symmetric balance pair) is released synchronously in the same physics step. The driver calls it
    /// ONLY when <see cref="Gameplay.Core.Mechanic.ShapeFillMechanicController.ReadyForNextRelease"/> is true, so
    /// the protocol stays "release group → wait for rest → release next". Returns false (and leaves
    /// <paramref name="taps"/> empty) when nothing remains to release.
    /// </summary>
    public interface ISimBotPolicy
    {
        bool TryNextRelease(GameModel model, List<Vec2> taps);
    }
}
