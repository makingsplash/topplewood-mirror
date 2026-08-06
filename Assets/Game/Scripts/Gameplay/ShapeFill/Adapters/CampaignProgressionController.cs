using System;
using Gameplay.Core;
using Gameplay.PlayScreen;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Drives the campaign progression + persistence (TASK-009, dev-architect 17:25 §4/§5). Owns the current
    /// campaign index: resumes it from <see cref="ICampaignStore"/> on start, and on each level resolution
    /// advances (WIN → next level, persisted immediately as a durable checkpoint) or retries the same level
    /// (LOSS → reload from scratch). Reload is reset-in-place via <see cref="GameController.ReloadLevel"/> —
    /// the mechanic's LevelLoaded handler already tears down + respawns, so no extra teardown is needed.
    /// In-level progress is deliberately NOT restored (ticket: kill mid-level = level from scratch); only the
    /// level number is persisted.
    ///
    /// TASK-116: this stays the single <c>LevelResolved</c> subscriber and the sole index/persist/reload owner,
    /// but no longer transitions inline — it hands its transition to <see cref="ILevelResolutionFlow"/> as a
    /// callback and the flow decides *when* to run it (prod: immediately, bit-for-bit the old behaviour; bringup:
    /// after the WIN/LOSE banner + 2s hold). The domain never sees the flow. Also implements
    /// <see cref="ICampaignRestart"/> so the play-screen Restart button reuses this same reset-in-place reload
    /// (no second index); restart is neutral — it does not resolve a loss or persist the pointer.
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
        private bool _completed;

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
            // "Campaign complete" is derived state now (TASK-044): recomputed against the LIVE level count each
            // session, never persisted, so BUG-1's stuck completed flag cannot exist. Starts false so the
            // one-shot completion log fires at most once per session.
            _completed = false;

            // GameController.Start already loaded index 0; only re-enter when resuming a later level.
            if (_index != 0)
                _controller.ReloadLevel(_index);

            _resolvedSubscription = _model.LevelResolved.Subscribe(_ => OnLevelResolved());
        }

        // Hand the transition to the flow instead of running it inline (TASK-116): the flow calls ApplyResolution
        // back when it decides the level should actually change (prod: now; bringup: after the banner + hold).
        private void OnLevelResolved()
            => _resolutionFlow.Present(_model.Outcome.CurrentValue, ApplyResolution);

        // The actual transition — unchanged from the pre-TASK-116 inline body. Reads phase live at transition
        // time; nothing re-resolves during the hold, so Won/Lost is stable.
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
                _index++;
                _store.WriteLevelIndex(_index); // durable WIN checkpoint (flushes prefs to disk)
                _controller.ReloadLevel(_index);
                return;
            }

            // Won the final level → campaign complete. The pointer is already at the last index (persisted when
            // we advanced into it); completion is derived, not stored, so there is nothing more to write. Leave
            // the finished figure on screen (post-last-level behaviour is an open design lever — out of scope).
            if (!_completed)
            {
                _completed = true;
                Debug.Log("[ShapeFill] CAMPAIGN COMPLETE");
            }
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
