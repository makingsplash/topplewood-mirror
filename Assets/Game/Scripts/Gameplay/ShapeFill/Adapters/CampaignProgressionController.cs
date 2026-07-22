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
            var progress = _store.Read();
            _index = Clamp(progress?.CampaignIndex ?? 0);
            _completed = progress?.CampaignCompleted ?? false;

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
                Persist();
                _controller.ReloadLevel(_index);
                return;
            }

            // Won the final level → campaign complete. Persist once; leave the finished figure on screen
            // (behaviour after the last level is an open design lever — GAME_RULES / A/B, out of scope here).
            if (!_completed)
            {
                _completed = true;
                Persist();
                Debug.Log("[ShapeFill] CAMPAIGN COMPLETE");
            }
        }

        private void Persist()
            => _store.Write(new CampaignProgress { CampaignIndex = _index, CampaignCompleted = _completed });

        private int Clamp(int index)
        {
            if (index < 0)
                return 0;
            var last = _levelSource.Count - 1;
            return index > last ? last : index;
        }

        public void Dispose()
        {
            _resolvedSubscription?.Dispose();
            _resolvedSubscription = null;
        }
    }
}
