using System;
using DataStorage;
using Gameplay.Configs;
using Gameplay.Core;
using R3;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
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
    /// Every win pays the full tier reward, a replay included (owner decision, TASK-432). The TASK-240 first-clear
    /// gate is gone on purpose: the campaign WRAPS at the end (<c>CampaignProgressionController</c> resets
    /// <c>_index=0</c> on the final win), so coins are now minted for as long as the player keeps winning — the
    /// owner accepted that knowingly.
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
        /// <c>GatedResolutionFlow</c>) to show "+N". Computed synchronously in <see cref="OnResolved"/> during the
        /// <c>LevelResolved</c> dispatch, i.e. well before the win screen's settle-beat delay elapses, so the screen
        /// always reads a fresh value regardless of subscriber order.</summary>
        public int LastReward { get; private set; }

        /// <summary>The reward rule, stateless: a Won resolution pays <see cref="CoreConfig.GetLevelReward"/> for the
        /// level's tier, anything else pays 0. No memory of earlier wins — the same level won twice pays twice.</summary>
        public static int RewardFor(GameModel model, CoreConfig core)
        {
            if (model.CurrentPhase != GamePhase.Won || model.CurrentLevel == null)
                return 0;

            return core.GetLevelReward(model.CurrentLevel.DifficultyTier);
        }

        public void Start()
        {
            _subscription = _model.LevelResolved.Subscribe(_ => OnResolved());
        }

        private void OnResolved()
        {
            if (_model.CurrentPhase != GamePhase.Won)
                return;

            var reward = RewardFor(_model, _core);
            LastReward = reward;

            if (reward > 0)
            {
                var goldBefore = Data.Meta.GoldAmount.Value;
                Data.Meta.GoldAmount.Value += reward;
                // Durable NOW (TASK-537): the Value setter only stages PlayerPrefs.SetInt — ForceSave is the same
                // SetInt again — and only PlayerPrefs.Save() reaches disk. Don't lean on the campaign checkpoint's
                // flush: it may run BEFORE this subscriber, and a kill in focus then loses the reward.
                PlayerPrefsCollection.Instance?.SaveDataIfChanged();
                GlobalService.Analytics.Resource.SendSoftIncome(reward, goldBefore, Placement.SoftWinLevelIncome);
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
