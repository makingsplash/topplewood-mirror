using Gameplay.Core;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// How a tour is built for a level (TASK-613 plan §3.7): the catalog holds a recipe per level id, and the runner
    /// builds the concrete <see cref="TutorialDefinition"/> from the LOADED level's data. A recipe is a class of tours,
    /// not one tour: the same recipe on another level yields that level's beats with no code change.
    /// </summary>
    public interface ITourRecipe
    {
        /// <summary>The tour's stable id (progress store + analytics funnel) — known without building, so the dev
        /// "Reset progress" can forget a tour on a level whose data it has not loaded.</summary>
        string Id { get; }

        /// <summary>The tour for <paramref name="level"/>, or null when the level lacks what the recipe needs (the
        /// recipe logs why — a tour never guesses).</summary>
        TutorialDefinition Build(LevelData level);
    }
}
