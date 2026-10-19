using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DataStorage;
using Gameplay.Configs;
using Gameplay.Core;
using R3;
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
    /// Both panels play their own show-animation, which is data on the prefab (<c>ScreenReveal</c>, TASK-425 — the
    /// "no fade" of TASK-121/155 is long gone, this summary said otherwise until TASK-613).
    ///
    /// Win interlude (TASK-613): if someone armed a <see cref="WinInterlude"/> (today: the tutorial's "Совпало" beat),
    /// the Won path plays THAT instead of the plain settle-beat and shows the panel when it ends — or after
    /// <see cref="PlayScreenConfig.MaxWinInterludeSeconds"/> with a Warning, so a broken number never hangs a win.
    /// Levels without an armed number keep the plain <see cref="PlayScreenConfig.WinSettleSeconds"/> beat.
    /// </summary>
    public sealed class GatedResolutionFlow : ILevelResolutionFlow, IDisposable
    {
        private readonly PlayScreenConfig _config;
        private readonly PlayScreenInteraction _interaction;
        private readonly LevelRewardService _reward;
        private readonly CoreConfig _core;
        private readonly IWinRewardDoubler _rewardDoubler;
        private readonly WinInterlude _interlude;
        private readonly CancellationTokenSource _cts = new();

        public GatedResolutionFlow(
            PlayScreenConfig config, PlayScreenInteraction interaction, LevelRewardService reward,
            CoreConfig core, IWinRewardDoubler rewardDoubler, WinInterlude interlude)
        {
            _config = config;
            _interaction = interaction;
            _reward = reward;
            _core = core;
            _rewardDoubler = rewardDoubler;
            _interlude = interlude;
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

            // An armed interlude (the tour's "Совпало" beat) replaces the settle-beat; otherwise the settle-beat on the
            // ASSEMBLED figure — the visual reward — before the panel (design 01 §2). Its number lives in
            // PlayScreenConfig.WinSettleSeconds (0.5 s since the owner's 2026-08-19 playtest — see the config); config,
            // not a constant — open tuning lever for game-balancer.
            if (_interlude.TryTake(out var play))
                await PlayInterludeAsync(play, ct);
            else
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

        // Wait for the number to complete OR for the ceiling, whichever first. The subscription is disposed on the way
        // out either way — that is what stops an over-long number before the panel shows.
        private async UniTask PlayInterludeAsync(Func<Observable<Unit>> play, CancellationToken ct)
        {
            var done = new UniTaskCompletionSource();
            using var subscription = play().Subscribe(_ => { }, _ => done.TrySetResult());
            var winner = await UniTask.WhenAny(
                done.Task,
                UniTask.Delay(TimeSpan.FromSeconds(_config.MaxWinInterludeSeconds), cancellationToken: ct));
            if (winner != 0)
                UnityEngine.Debug.unityLogger.Log(UnityEngine.LogType.Warning,
                    $"[PlayScreen] win interlude did not finish in {_config.MaxWinInterludeSeconds:0.#}s — showing the win screen");
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
