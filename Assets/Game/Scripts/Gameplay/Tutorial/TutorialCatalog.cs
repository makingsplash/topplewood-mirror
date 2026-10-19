using System.Collections.Generic;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Per-level tutorial registry (TASK-377, closing the TASK-078/243 hardcode): maps a level's <c>LevelId</c> to
    /// the RECIPE of the tour to play on it (TASK-613: a recipe, not a pre-built tour — the beats come from the loaded
    /// level's data, see <see cref="ITourRecipe"/>). Keyed by <c>LevelId</c> (not campaign index) so a dev level-jump
    /// or a re-ordering of the manifest never mis-fires the tour — the same robustness reason the id-based gate
    /// replaced the index gate (TASK-112). A level absent from the catalog plays no tour (<see cref="Resolve"/>
    /// returns null). Every catalog level must carry a non-empty <c>winningOrder</c>; the catalog lives in code, so
    /// an EditMode test holds that invariant against the manifest (<c>TourCatalogDataTests</c>).
    /// </summary>
    public sealed class TutorialCatalog
    {
        private readonly IReadOnlyDictionary<string, ITourRecipe> _byLevelId;

        public TutorialCatalog(IReadOnlyDictionary<string, ITourRecipe> byLevelId) => _byLevelId = byLevelId;

        /// <summary>Level ids that have a tour — for the manifest invariant test.</summary>
        public IEnumerable<string> LevelIds => _byLevelId.Keys;

        /// <summary>The tour recipe registered for <paramref name="levelId"/>, or null if the level has none.</summary>
        public ITourRecipe Resolve(string levelId) =>
            levelId != null && _byLevelId.TryGetValue(levelId, out var recipe) ? recipe : null;
    }
}
