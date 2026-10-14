using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DataStorage;
using Gameplay.Configs;
using Gameplay.Core;
using Services;
using Services.Gameplay;
using Ui;
using Ui.Screens;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Production <see cref="ILevelResolutionFlow"/> (TASK-154 → TASK-157). Gates BOTH terminal outcomes with a
    /// tap-gated screen, each after a short settle-beat — the player never gets slammed straight to a reload:
    /// <list type="bullet">
    /// <item><c>Lost</c> → <see cref="LoseScreen"/>, waits for the "Try again" tap (TASK-154, unchanged).</item>
    /// <item><c>Won</c> → the settle-beat lingers on the ASSEMBLED figure (the visual reward, design §2), then
    /// <see cref="Ui.Screens.WinScreen"/> waits for the "Continue"/"Next" tap (TASK-157; "Next" under "ALL DONE!" on
    /// the last line-up level, TASK-528). This replaces the old
    /// silent auto-advance.</item>
    /// </list>
    /// The tap runs <paramref name="applyTransition"/> (the campaign controller's own advance/retry path). The
    /// flow never duplicates the "won ? advance : reload" logic — that stays the single source of truth in
    /// <see cref="Gameplay.Adapters.CampaignProgressionController"/>; the screen decides *when*, not *what*. The
    /// <c>isFinalLevel</c> flag is presentational only (win-copy fork) — it never changes the transition.
    ///
    /// While a panel is pending/up, <see cref="PlayScreenInteraction.IsBusy"/> is true, so
    /// <see cref="RestartButtonPresenter"/> disables the RESTART button and a manual restart cannot race the
    /// tap-driven transition. IsBusy is cleared at the moment the transition actually runs.
    ///
    /// Teardown mid-beat (scope/session dispose): the shared <see cref="CancellationToken"/> is cancelled, the
    /// awaited settle-beat throws <see cref="OperationCanceledException"/> (swallowed by the fire-and-forget
    /// UniTaskVoid), the panel is never shown and the transition is (correctly) NOT run. Only ever one resolution
    /// is live at a time — the domain is frozen at Won/Lost until the transition, and the next resolution cannot
    /// fire before it — so no overlap. For EVERY Won (the final line-up level included since TASK-528) the durable
    /// checkpoint is already written on resolve (CampaignProgressionController), so a teardown before the tap does NOT
    /// lose the win — relaunch resumes the next level (after the last line-up level: the first pool level).
    ///
    /// No fade on either panel: <c>IWindowAnimator</c> is absent (TASK-121/155) — v1 shows instantly; the win
    /// "celebration" is carried by the figure reveal in the settle-beat + the warm copy, not by animation.
    /// </summary>
    // NOTE (TASK-157): the file is still named LoseResolutionFlow.cs; the class was renamed in place because a
    // `git mv` was not available in the implementer run (Bash denied). It is a plain-C# class (not a
    // MonoBehaviour) so no scene/prefab GUID references it — the filename is cosmetic. Manual finishing step:
    // `git mv LoseResolutionFlow.cs GatedResolutionFlow.cs` (+ .meta), no content change.
    public sealed class GatedResolutionFlow : ILevelResolutionFlow, IDisposable
    {
        private readonly PlayScreenConfig _config;
        private readonly PlayScreenInteraction _interaction;
        private readonly LevelRewardService _reward;
        private readonly CoreConfig _core;
        private readonly IWinRewardDoubler _rewardDoubler;
        private readonly CancellationTokenSource _cts = new();

        public GatedResolutionFlow(
            PlayScreenConfig config, PlayScreenInteraction interaction, LevelRewardService reward,
            CoreConfig core, IWinRewardDoubler rewardDoubler)
        {
            _config = config;
            _interaction = interaction;
            _reward = reward;
            _core = core;
            _rewardDoubler = rewardDoubler;
        }

        public void Present(GameOutcome outcome, Action applyTransition, bool isFinalLevel)
        {
            // Both outcomes are now gated by a screen (TASK-157). Any non-terminal outcome still transitions
            // immediately (defensive default, as before for the non-Lost case).
            switch (outcome)
            {
                case GameOutcome.Won:
                    PresentWonAsync(applyTransition, isFinalLevel, _cts.Token).Forget();
                    return;
                case GameOutcome.Lost:
                    PresentLostAsync(applyTransition, _cts.Token).Forget();
                    return;
                default:
                    applyTransition?.Invoke();
                    return;
            }
        }

        private async UniTaskVoid PresentWonAsync(Action applyTransition, bool isFinalLevel, CancellationToken ct)
        {
            _interaction.IsBusy.Value = true;

            // Settle-beat on the ASSEMBLED figure — the visual reward — before the panel (design 01 §2). Number
            // lives in PlayScreenConfig.WinSettleSeconds (v1 = 1.4s, owner decision: a touch longer than the lose
            // beat, to "savour" the win); config, not a constant — open tuning lever for game-balancer.
            await UniTask.Delay(TimeSpan.FromSeconds(_config.WinSettleSeconds), cancellationToken: ct);

            var screen = await Service.UI.Show<WinScreen>(ScreenNames.WinScreen);

            // The faucet (LevelRewardService) computed this synchronously during the LevelResolved dispatch, i.e.
            // before this settle-beat delay elapsed, so LastReward is always the value for the level just won
            // (TASK-240, architect §8). Every win pays since TASK-432, a replay included; 0 only under a misconfigured
            // reward table → the screen hides the reward row.
            var reward = _reward.LastReward;
            var x2Available = _core.WinScreenX2Enabled
                              && Data.Meta.CurrentLevel.Value >= _core.WinScreenX2ButtonStartLevel;

            screen.Setup(onNext: () =>
            {
                applyTransition?.Invoke();
                _interaction.IsBusy.Value = false;
            }, isFinalLevel, reward, x2Available, onX2: () => _rewardDoubler.TryDouble(reward, _ => { }));
        }

        private async UniTaskVoid PresentLostAsync(Action applyTransition, CancellationToken ct)
        {
            _interaction.IsBusy.Value = true;

            // Settle-beat before the panel appears (design 01-game-designer.md §96, range 1.0–1.5s; number lives
            // in PlayScreenConfig.LoseSettleSeconds, not a constant — open tuning lever for game-balancer against
            // the working screen). The panel's own show-animation is data on the prefab (ScreenReveal, TASK-425).
            await UniTask.Delay(TimeSpan.FromSeconds(_config.LoseSettleSeconds), cancellationToken: ct);

            var screen = await Service.UI.Show<LoseScreen>(ScreenNames.LoseScreen);
            screen.Setup(onTryAgain: () =>
            {
                applyTransition?.Invoke();
                _interaction.IsBusy.Value = false;
            });
        }

        public void Dispose()
        {
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            _cts.Dispose();
        }
    }
}
