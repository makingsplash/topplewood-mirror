using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gameplay.Core;
using Services;
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
    /// <see cref="Ui.Screens.WinScreen"/> waits for the "Next"/"Play again" tap (TASK-157). This replaces the old
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
    /// fire before it — so no overlap. For a non-final Won the durable checkpoint is already written on resolve
    /// (CampaignProgressionController), so a teardown before the tap does NOT lose the win — relaunch resumes N+1.
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
        private readonly CancellationTokenSource _cts = new();

        public GatedResolutionFlow(PlayScreenConfig config, PlayScreenInteraction interaction)
        {
            _config = config;
            _interaction = interaction;
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
            screen.Setup(onNext: () =>
            {
                applyTransition?.Invoke();
                _interaction.IsBusy.Value = false;
            }, isFinalLevel);
        }

        private async UniTaskVoid PresentLostAsync(Action applyTransition, CancellationToken ct)
        {
            _interaction.IsBusy.Value = true;

            // Settle-beat before the panel appears (design 01-game-designer.md §96, range 1.0–1.5s; number lives
            // in PlayScreenConfig.LoseSettleSeconds, not a constant — open tuning lever for game-balancer against
            // the working screen). No fade: IWindowAnimator is absent (TASK-121), so v1 shows instantly.
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
