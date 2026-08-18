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
    /// Production <see cref="ILevelResolutionFlow"/> (TASK-154). A <c>Won</c> transitions immediately, exactly as
    /// before (the level auto-advances the instant it resolves). A <c>Lost</c> is gated: after a short settle-beat
    /// — so the player sees WHAT they missed instead of the level slamming to a reload — it shows
    /// <see cref="LoseScreen"/> and waits for the "Try again" tap, then runs <paramref name="applyTransition"/>
    /// (the campaign controller's own Lost-retry path). The flow never duplicates the "won ? advance : reload"
    /// logic — that stays the single source of truth in
    /// <see cref="Gameplay.Adapters.CampaignProgressionController"/>; the screen decides *when*, not *what*.
    ///
    /// While the lose panel is pending/up, <see cref="PlayScreenInteraction.IsBusy"/> is true, so
    /// <see cref="RestartButtonPresenter"/> disables the RESTART button and a manual restart cannot race the
    /// tap-driven transition. IsBusy is cleared at the moment the transition actually runs.
    ///
    /// Teardown mid-beat (scope/session dispose): the shared <see cref="CancellationToken"/> is cancelled, the
    /// awaited settle-beat throws <see cref="OperationCanceledException"/> (swallowed by the fire-and-forget
    /// UniTaskVoid), the panel is never shown and the transition is (correctly) NOT run. Only ever one resolution
    /// is live at a time — the domain is frozen at Won/Lost until the transition, and the next resolution cannot
    /// fire before it — so no overlap.
    /// </summary>
    public sealed class LoseResolutionFlow : ILevelResolutionFlow, IDisposable
    {
        private readonly PlayScreenConfig _config;
        private readonly PlayScreenInteraction _interaction;
        private readonly CancellationTokenSource _cts = new();

        public LoseResolutionFlow(PlayScreenConfig config, PlayScreenInteraction interaction)
        {
            _config = config;
            _interaction = interaction;
        }

        public void Present(GameOutcome outcome, Action applyTransition)
        {
            // Only a loss gets the screen; a win auto-advances the instant it resolves, bit-for-bit as before.
            if (outcome != GameOutcome.Lost)
            {
                applyTransition?.Invoke();
                return;
            }

            PresentLostAsync(applyTransition, _cts.Token).Forget();
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
