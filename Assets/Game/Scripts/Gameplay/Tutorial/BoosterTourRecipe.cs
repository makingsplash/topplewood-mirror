using Game.Services.LogService;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// "Meet a booster" tour (TASK-635 plan §3.4): a HUD beat on the booster's button (<see cref="HudTapStep"/>), then a
    /// SILENT gate on the first piece of the level's certified <see cref="LevelData.WinningOrder"/> — the booster's own
    /// outline points at it, the tour only keeps the other pieces shut (<see cref="TapPieceStep"/> silent). The tour ends
    /// on that piece's release: no win step — the player has met the booster, the rest of the level is theirs.
    /// The class "a HUD booster acting on the next right piece": another such booster, or the Hint on another candidate
    /// level (an A/B of the place, design 01 §9), is one catalog line. No order → no tour, loudly, as
    /// <see cref="AssembleTourRecipe"/>.
    /// </summary>
    public sealed class BoosterTourRecipe : ITourRecipe
    {
        /// <summary>The Hint booster's tour (progress id + analytics funnel). Read by <c>HintUnlockController</c>.</summary>
        public const string HintTourId = "hint_tutorial";

        /// <summary>The Hint button's <see cref="IHudTourTarget.Id"/>.</summary>
        public const string HintButtonTargetId = "hint_button";

        private readonly string _tourId;
        private readonly string _hudTargetId;

        public BoosterTourRecipe(string tourId, string hudTargetId)
        {
            _tourId = tourId;
            _hudTargetId = hudTargetId;
        }

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

            return new TutorialDefinition(_tourId, new ITutorialStep[]
            {
                new HudTapStep(_hudTargetId),
                new TapPieceStep(order[0], 1, silent: true),
            });
        }
    }
}
