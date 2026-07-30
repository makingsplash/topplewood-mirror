using System;
using Gameplay.Core;
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
    /// </summary>
    public sealed class CampaignProgressionController : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly GameController _controller;
        private readonly ILevelSource _levelSource;
        private readonly ICampaignStore _store;

        private IDisposable _resolvedSubscription;
        private int _index;
        private bool _completed;

        public CampaignProgressionController(
            GameModel model, GameController controller, ILevelSource levelSource, ICampaignStore store)
        {
            _model = model;
            _controller = controller;
            _levelSource = levelSource;
            _store = store;
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

        private void OnLevelResolved()
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

        private int Clamp(int index)
        {
            if (index < 0)
                return 0;
            var last = _levelSource.Count - 1;
            return index > last ? last : index;
        }

#if GPIE_DEVELOPMENT
        /// <summary>
        /// Dev/QA cheat hook (TASK-011): the current campaign index, so the level-switch overlay can compute
        /// prev/next and highlight the active level. Editor/dev builds only — never compiled into player builds.
        /// </summary>
        public int DevCampaignIndex => _index;

        /// <summary>
        /// Dev/QA cheat hook (TASK-011): jump straight to <paramref name="index"/> and keep the campaign
        /// pointer in sync, so the next WIN/LOSS resolves relative to the jumped-to level instead of snapping
        /// back to the pre-jump index. Clamped to the level range; reuses the normal reset-in-place reload path
        /// (no gameplay-logic change). Deliberately does NOT persist — the cheat is non-destructive to saved
        /// progress; a level actually resolved after the jump persists through the usual flow. Editor/dev only.
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
