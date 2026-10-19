using System;
using Gameplay.Core;
using Gameplay.Tutorial;
using R3;
using Services.Gameplay;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Unlocks the Hint booster (TASK-635 plan §3.1) — prod-only, beside the button. On every
    /// <see cref="GameModel.LevelLoaded"/> (and once on Start for the level already loaded, the
    /// <see cref="TutorialRunner"/> idiom) it asks <see cref="HintUnlockRules.Decide"/> and applies the answer through
    /// <see cref="HintBoosterService.Unlock"/>. The tour's level comes from the catalog (<see cref="TutorialCatalog.LevelIdOf"/>),
    /// its place from the CURRENT line-up by id, cached for the session — so re-ordering the manifest moves the rule with it.
    ///
    /// A silent unlock also marks the tour completed: otherwise a pool replay of the tour's level would raise the tour for
    /// a player who never needed it. That mark must land BEFORE the runner decides on the same LevelLoaded delivery, so
    /// this entry point is registered before the installers (<c>GameplayLifetimeScope</c>, "порядок несущий"): VContainer
    /// starts entry points in registration order, and an earlier Start subscribes earlier. Steps never grant — the rule
    /// runs here, also where no tour plays (DisableTutorial, a level without an order: the player still gets the button).
    /// </summary>
    public sealed class HintUnlockController : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly HintBoosterService _hint;
        private readonly ICampaignStore _campaign;
        private readonly ILevelSource _levels;
        private readonly TutorialCatalog _catalog;
        private readonly ITutorialProgressStore _progress;

        private IDisposable _subscription;
        private string _tourLevelId;
        private int _tourLineupIndex = -2; // -2 = not resolved yet; -1 = not in the line-up
        private bool _done;

        public HintUnlockController(GameModel model, HintBoosterService hint, ICampaignStore campaign,
            ILevelSource levels, TutorialCatalog catalog, ITutorialProgressStore progress)
        {
            _model = model;
            _hint = hint;
            _campaign = campaign;
            _levels = levels;
            _catalog = catalog;
            _progress = progress;
        }

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => Evaluate());
            Evaluate();
        }

        private void Evaluate()
        {
            if (_done || _model.CurrentLevel == null)
                return;

            ResolveTourPlace();
            var tourId = BoosterTourRecipe.HintTourId;
            var unlocked = _hint.Unlocked.CurrentValue;
            var completed = _progress.IsCompleted(tourId);
            if (unlocked && completed)
            {
                _done = true; // nothing can change any more this session
                return;
            }

            var onTourLevel = _tourLevelId != null && _model.CurrentLevel.LevelId == _tourLevelId;
            var action = HintUnlockRules.Decide(unlocked, completed, onTourLevel, _tourLineupIndex, _campaign.Read());
            switch (action)
            {
                case HintUnlockAction.UnlockSilent:
                    _hint.Unlock(introGrant: false);
                    _progress.MarkCompleted(tourId);
                    Debug.unityLogger.Log(LogType.Log,
                        $"[booster] unlock silent ({(_tourLineupIndex < 0 ? "missing" : "passed")}) charges={_hint.Charges.CurrentValue}");
                    break;
                case HintUnlockAction.UnlockWithGrant:
                    _hint.Unlock(introGrant: true);
                    Debug.unityLogger.Log(LogType.Log,
                        $"[booster] unlock with grant on '{_tourLevelId}' charges={_hint.Charges.CurrentValue}");
                    break;
            }
        }

        private void ResolveTourPlace()
        {
            if (_tourLineupIndex != -2)
                return;

            _tourLevelId = _catalog.LevelIdOf(BoosterTourRecipe.HintTourId);
            _tourLineupIndex = -1;
            if (_tourLevelId != null)
                for (var i = 0; i < _levels.Count; i++)
                    if (_levels.Load(i)?.LevelId == _tourLevelId)
                    {
                        _tourLineupIndex = i;
                        break;
                    }

            if (_tourLineupIndex < 0)
                // The button must never stay hidden forever because of data: unlock silently (rule 1) and say why.
                Debug.unityLogger.Log(LogType.Error,
                    $"[booster] hint tour level '{_tourLevelId ?? "<none in catalog>"}' is not in the line-up — unlocking without a tour");
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
