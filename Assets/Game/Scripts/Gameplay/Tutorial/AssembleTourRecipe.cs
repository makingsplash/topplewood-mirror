using System.Collections.Generic;
using Game.Services.LogService;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// "Assemble the picture" tour (TASK-613): one <see cref="TapPieceStep"/> per entry of the level's certified
    /// <see cref="LevelData.WinningOrder"/> (ordinal 1..n), then <see cref="AwaitWinStep"/> (TASK-634; was the
    /// "Совпало" beat). Replaces the hardcoded
    /// <c>new[] {0, 1}</c> sailboat order (TASK-078/079 debt): any level put in the catalog with this recipe gets its
    /// own beats from its own data. No order → no tour, loudly: the tour must not invent an order the sim never
    /// certified (the same rule as the Hint booster's "never guess").
    /// </summary>
    public sealed class AssembleTourRecipe : ITourRecipe
    {
        private readonly string _tourId;

        /// <param name="tourId">Stable id shared by the progress store and the analytics funnel.</param>
        public AssembleTourRecipe(string tourId) => _tourId = tourId;

        public string Id => _tourId;

        public TutorialDefinition Build(LevelData level)
        {
            var order = level?.WinningOrder;
            if (order == null || order.Count == 0)
            {
                Debug.unityLogger.Log(LogType.Error,
                    $"[{LogCategory.Tutorial}] Tour '{_tourId}' on level '{level?.LevelId}' has no winningOrder — no tour");
                return null;
            }

            var steps = new List<ITutorialStep>(order.Count + 1);
            for (var i = 0; i < order.Count; i++)
                steps.Add(new TapPieceStep(order[i], i + 1));
            steps.Add(new AwaitWinStep());
            return new TutorialDefinition(_tourId, steps);
        }
    }
}
