using System;
using Gameplay.Core;
using Gameplay.PlayScreen;
using R3;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Drives the campaign progression + persistence (TASK-009, dev-architect 17:25 §4/§5). Owns the current
    /// campaign index: resumes it from <see cref="ICampaignStore"/> on start, and on each level resolution
    /// advances (WIN → next level) or retries the same level (LOSS → reload from scratch). Reload is
    /// reset-in-place via <see cref="GameController.ReloadLevel"/> — the mechanic's LevelLoaded handler already
    /// tears down + respawns, so no extra teardown is needed. In-level progress is deliberately NOT restored
    /// (ticket: kill mid-level = level from scratch); only the level number is persisted.
    ///
    /// TASK-116: this stays the single <c>LevelResolved</c> subscriber and the sole index/persist/reload owner,
    /// but no longer transitions inline — it hands its transition to <see cref="ILevelResolutionFlow"/> as a
    /// callback and the flow decides *when* to run it (prod TASK-157: after the win/lose panel tap; bringup:
    /// after the WIN/LOSE banner + 2s hold). The domain never sees the flow.
    ///
    /// TASK-157 splits WHEN the pointer is persisted from WHEN the level navigates, in this single owner:
    /// <list type="bullet">
    /// <item><b>Durable win (Q2):</b> a non-final WIN persists the checkpoint (<c>index+1</c>) at the moment of
    /// resolution (<see cref="OnLevelResolved"/>), NOT on the "Next" tap — so a player who wins, does not tap and
    /// kills the app relaunches on N+1. The final level does NOT persist on resolve (it would write the future
    /// wrap-0 and drop all progress on a kill) — only the tap does.</item>
    /// <item><b>Last-level wrap (Q1):</b> winning the final level wraps the campaign back to level 1
    /// (<c>_index = 0</c>, persisted) instead of the old terminal no-op log (the TASK-138 dead-end). v1 does NOT
    /// persist a "campaign completed" fact — the wrap is indistinguishable from a new player (tech-debt with a
    /// map/meta trigger, RFC §12).</item>
    /// </list>
    /// Also implements <see cref="ICampaignRestart"/> so the play-screen Restart button reuses this same
    /// reset-in-place reload (no second index); restart is neutral — it does not resolve a loss or persist.
    /// </summary>
    public sealed class CampaignProgressionController : IStartable, IDisposable, ICampaignRestart
    {
        private readonly GameModel _model;
        private readonly GameController _controller;
        private readonly ILevelSource _levelSource;
        private readonly ICampaignStore _store;
        private readonly ILevelResolutionFlow _resolutionFlow;

        private IDisposable _resolvedSubscription;
        private int _index;

        public CampaignProgressionController(
            GameModel model, GameController controller, ILevelSource levelSource, ICampaignStore store,
            ILevelResolutionFlow resolutionFlow)
        {
            _model = model;
            _controller = controller;
            _levelSource = levelSource;
            _store = store;
            _resolutionFlow = resolutionFlow;
        }

        public void Start()
        {
            _index = Clamp(_store.ReadLevelIndex());

            // GameController.Start already loaded index 0; only re-enter when resuming a later level.
            if (_index != 0)
                _controller.ReloadLevel(_index);

            _resolvedSubscription = _model.LevelResolved.Subscribe(_ => OnLevelResolved());
        }

        // Hand the transition to the flow instead of running it inline (TASK-116): the flow calls ApplyResolution
        // back when it decides the level should actually change (prod TASK-157: on the panel tap; bringup: after
        // the banner + hold).
        private void OnLevelResolved()
        {
            var won = _model.CurrentPhase == GamePhase.Won;          // same authority ApplyResolution reads (phase live)
            var isFinal = _index + 1 >= _levelSource.Count;

            // Durable win (TASK-157 Q2): persist the checkpoint at the MOMENT OF RESOLUTION, not on the "Next"
            // tap — win N, don't tap, kill the app → relaunch resumes N+1 (Start reads the store). Only a
            // NON-final win: persisting on the final level's resolve would write the future wrap-0 and drop all
            // progress on a kill (RFC §0.2). ApplyResolution then only advances the in-memory pointer, no re-write.
            if (won && !isFinal)
                _store.WriteLevelIndex(_index + 1);

            _resolutionFlow.Present(_model.Outcome.CurrentValue, ApplyResolution, isFinal);
        }

        // The actual transition. Reads phase live at transition time; nothing re-resolves during the hold, so
        // Won/Lost is stable.
        private void ApplyResolution()
        {
            if (_model.CurrentPhase != GamePhase.Won)
            {
                // LOSS → retry the same level from scratch (no in-level restore).
                _controller.ReloadLevel(_index);
                return;
            }

            if (_index + 1 < _levelSource.Count)
            {
                // Non-final WIN → advance. The durable checkpoint was already written on resolve (Q2); here we
                // only move the in-memory pointer and navigate, so there is no double write.
                _index++;
                _controller.ReloadLevel(_index);
                return;
            }

            // Won the final level → wrap the campaign back to level 1 (TASK-157 Q1, replaces the TASK-138 terminal
            // no-op dead-end). Persist HERE, on the explicit "Play again" tap (NOT on resolve — RFC §0.2), so a
            // kill before the tap keeps the player on the final level instead of rolling back to 0. v1 does not
            // persist a "campaign completed" fact — the wrap is indistinguishable from a new player (tech-debt).
            _index = 0;
            _store.WriteLevelIndex(_index);
            _controller.ReloadLevel(_index);
        }

        /// <summary>
        /// <see cref="ICampaignRestart"/> — restart the current level for the play-screen Restart button
        /// (TASK-116, Q3). Reuses the reset-in-place reload at the current index (the same path <see cref="Start"/>
        /// and the dev jump use), so there is no second index. Neutral for the domain: does NOT call
        /// <c>Resolve(Lost)</c>, is not counted as a loss/attempt, and does not persist the pointer.
        /// </summary>
        public void ReloadCurrent() => _controller.ReloadLevel(_index);

        private int Clamp(int index)
        {
            if (index < 0)
                return 0;
            var last = _levelSource.Count - 1;
            return index > last ? last : index;
        }

#if GPIE_DEVELOPMENT
        /// <summary>
        /// Dev/QA cheat hook (TASK-011; consumed by the DDM host <c>BringupDebugMenuHost</c> since TASK-047):
        /// the current campaign index, so the level-switch menu can compute prev/next and show the active level.
        /// Editor/dev builds only — never compiled into player builds.
        /// </summary>
        public int DevCampaignIndex => _index;

        /// <summary>
        /// Dev/QA cheat hook (TASK-011; consumed by the DDM host <c>BringupDebugMenuHost</c> since TASK-047):
        /// jump straight to <paramref name="index"/> and keep the campaign pointer in sync, so the next WIN/LOSS
        /// resolves relative to the jumped-to level instead of snapping back to the pre-jump index. Clamped to
        /// the level range; reuses the normal reset-in-place reload path (no gameplay-logic change). Deliberately
        /// does NOT persist — the cheat is non-destructive to saved progress; a level actually resolved after the
        /// jump persists through the usual flow. Editor/dev only.
        /// </summary>
        public void DevJumpToLevel(int index)
        {
            _index = Clamp(index);
            _controller.ReloadLevel(_index);
        }
#endif

        public void Dispose()
        {
            _resolvedSubscription?.Dispose();
            _resolvedSubscription = null;
        }
    }
}
