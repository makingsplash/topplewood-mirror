using System;
using DataStorage;
using Gameplay.Configs;
using Gameplay.Core;
using R3;
using Tools.Constants;
using VContainer.Unity;

namespace Services.Gameplay
{
    /// <summary>
    /// Level-reward faucet (TASK-240, architect §2). Credits coins once per WON resolution — the same
    /// once-per-win signal (<see cref="GameModel.LevelResolved"/>) the durable checkpoint rides, so "one grant per
    /// win" holds by construction and a re-entry of the win panel cannot double-pay (the domain resolve, not a
    /// panel tap, is the trigger). Prod-only: registered in <c>GameplayLifetimeScope</c>, NOT in the shared
    /// <c>GameplayCoreInstaller</c>, so the sim never builds it and the "economy is sim-invisible" invariant holds
    /// (poseHash byte-for-byte).
    ///
    /// The reward writes <c>Meta.GoldAmount</c> DIRECTLY (architect §0.4), outside <c>#if SAYKIT_PURCHASING</c> —
    /// the IAP <c>RewardType.Coins</c> path is compiled out in a non-purchasing build, so routing the faucet
    /// through it would make the crane dead there. The wallet counter (<c>CoinsCounterComponent</c>) is subscribed
    /// to <c>GoldAmount.OnChanged</c>, so the HUD updates itself.
    ///
    /// First-clear gate (architect §5): the campaign WRAPS at the end (<c>CampaignProgressionController</c> resets
    /// <c>_index=0</c> on the final win, NOT a clamp), so a naive per-win faucet would mint coins forever on replay.
    /// Full reward fires only the first time a level id is cleared; a replay pays <c>ReplayReward</c> (config,
    /// default 0). Keyed by the stable <c>LevelData.LevelId</c> (the identity <c>LevelFailStreak</c> also uses), so
    /// it is reorder-safe and independent of the campaign-index pointer race.
    /// </summary>
    public sealed class LevelRewardService : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly CoreConfig _core;

        private IDisposable _subscription;

        public LevelRewardService(GameModel model, CoreConfig core)
        {
            _model = model;
            _core = core;
        }

        /// <summary>Coins credited on the level that just resolved Won — read by the win screen (through
        /// <c>GatedResolutionFlow</c>) to show "+N". 0 on a replay (already-rewarded level, <c>ReplayReward</c>
        /// default 0), which the screen renders by hiding the reward row. Computed synchronously in
        /// <see cref="OnResolved"/> during the <c>LevelResolved</c> dispatch, i.e. well before the win screen's
        /// settle-beat delay elapses, so the screen always reads a fresh value regardless of subscriber order.</summary>
        public int LastReward { get; private set; }

        public void Start()
        {
            _subscription = _model.LevelResolved.Subscribe(_ => OnResolved());
        }

        private void OnResolved()
        {
            if (_model.CurrentPhase != GamePhase.Won)
                return;

            var level = _model.CurrentLevel;
            var levelId = level == null ? null : level.LevelId;

            var firstClear = !string.IsNullOrEmpty(levelId)
                             && !Data.Meta.RewardedLevelIds.Value.Contains(levelId);

            var reward = firstClear ? _core.GetLevelReward(level.DifficultyTier) : _core.ReplayReward;
            LastReward = reward;

            if (reward > 0)
            {
                var goldBefore = Data.Meta.GoldAmount.Value;
                Data.Meta.GoldAmount.Value += reward;
                Data.Meta.GoldAmount.ForceSave();
                GlobalService.Analytics.Resource.SendSoftIncome(reward, goldBefore, Placement.SoftWinLevelIncome);
            }

            if (firstClear)
            {
                Data.Meta.RewardedLevelIds.Add(levelId);
                Data.Meta.RewardedLevelIds.ForceSave();
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
