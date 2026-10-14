using System.Collections.Generic;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Per-level tutorial registry (TASK-377, closing the TASK-078/243 hardcode): maps a level's <c>LevelId</c> to
    /// the tour to play on it. Replaces the single global <see cref="TutorialDefinition"/> the runner used to hold —
    /// <see cref="TutorialRunner"/> now resolves the tour for the LOADED level's id, so different levels teach
    /// different gestures (sailboat → assemble-in-order FTUE; the grand_seesaw two-finger tour was removed in
    /// TASK-549). Keyed by
    /// <c>LevelId</c> (not campaign index) so a dev level-jump or a re-ordering of the manifest never mis-fires the
    /// tour — the same robustness reason the id-based gate replaced the index gate (TASK-112). A level absent from
    /// the catalog plays no tour (<see cref="Resolve"/> returns null).
    /// </summary>
    public sealed class TutorialCatalog
    {
        private readonly IReadOnlyDictionary<string, TutorialDefinition> _byLevelId;

        public TutorialCatalog(IReadOnlyDictionary<string, TutorialDefinition> byLevelId) => _byLevelId = byLevelId;

        /// <summary>The tour registered for <paramref name="levelId"/>, or null if the level has none.</summary>
        public TutorialDefinition Resolve(string levelId) =>
            levelId != null && _byLevelId.TryGetValue(levelId, out var definition) ? definition : null;
    }
}
