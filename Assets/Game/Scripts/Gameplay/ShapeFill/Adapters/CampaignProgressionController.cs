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
    /// <see cref="CampaignProgress"/>: resumes it from <see cref="ICampaignStore"/> on start, and on each level
    /// resolution advances (WIN → next level) or retries the same level (LOSS → reload from scratch). Reload is
    /// reset-in-place via <see cref="GameController.ReloadLevel"/> — the mechanic's LevelLoaded handler already
    /// tears down + respawns, so no extra teardown is needed. In-level progress is deliberately NOT restored
    /// (ticket: kill mid-level = level from scratch); only the progress numbers are persisted.
    ///
    /// TASK-116: this stays the single <c>LevelResolved</c> subscriber and the sole progress/persist/reload owner,
    /// but no longer transitions inline — it hands its transition to <see cref="ILevelResolutionFlow"/> as a
    /// callback and the flow decides *when* to run it (prod TASK-157: after the win/lose panel tap; bringup:
    /// after the WIN/LOSE banner + 2s hold). The domain never sees the flow.
    ///
    /// TASK-528 — the pool after the line-up. WHICH level comes next is decided by the pure
    /// <see cref="CampaignCursor"/> (rule table: first pass → pool circle, plus catch-up when the line-up grew); this
    /// class only applies it. Winning the last line-up level no longer wraps to level 1 (the TASK-157 Q1 wrap is gone):
    /// it enters the <see cref="LevelPool"/> circle and the shown number keeps growing (81, 82…).
    /// <list type="bullet">
    /// <item><b>Durable win (TASK-157 Q2):</b> EVERY win persists its checkpoint at the moment of resolution
    /// (<see cref="OnLevelResolved"/>), NOT on the "Next" tap — so a player who wins, does not tap and kills the app
    /// relaunches on the next level. TASK-528 dropped the old final-level exception (it waited for the tap because
    /// the wrap-to-0 written on resolve would have erased progress on a kill): the transition only moves forward now,
    /// so the final win is an ordinary durable win. Consequence, by design: a kill before the tap does not show
    /// "ALL DONE!" again — the relaunch opens the first pool level.</item>
    /// </list>
    /// Also implements <see cref="ICampaignRestart"/> so the play-screen Restart button reuses this same
    /// reset-in-place reload (no second pointer); restart is neutral — it does not resolve a loss or persist.
    /// </summary>
    public sealed class CampaignProgressionController : IStartable, IDisposable, ICampaignRestart
    {
        private readonly GameModel _model;
        private readonly GameController _controller;
        private readonly ILevelSource _levelSource;
        private readonly ICampaignStore _store;
        private readonly ILevelResolutionFlow _resolutionFlow;
        private readonly LevelPool _pool;

        private IDisposable _resolvedSubscription;
        private CampaignProgress _progress = CampaignProgress.Fresh;
        private CampaignPosition _position;
        private CampaignProgress _pendingWin;

        public CampaignProgressionController(
            GameModel model, GameController controller, ILevelSource levelSource, ICampaignStore store,
            ILevelResolutionFlow resolutionFlow, LevelPool pool)
        {
            _model = model;
            _controller = controller;
            _levelSource = levelSource;
            _store = store;
            _resolutionFlow = resolutionFlow;
            _pool = pool;
        }

        public void Start()
        {
            // A broken pool never dead-ends (it falls back to the whole line-up), but it must not be silent either.
            for (var i = 0; i < _pool.Problems.Count; i++)
                Debug.LogError($"[TASK-528] {LevelPool.PoolFileName}: {_pool.Problems[i]}");

            _progress = _store.Read();
            _position = CampaignCursor.Resolve(_progress, _levelSource.Count, _pool);

            // GameController.Start already loaded index 0; only re-enter when resuming another level.
            if (_position.LineupIndex != 0)
                _controller.ReloadLevel(_position.LineupIndex);

            _resolvedSubscription = _model.LevelResolved.Subscribe(_ => OnLevelResolved());
        }

        // Hand the transition to the flow instead of running it inline (TASK-116): the flow calls ApplyResolution
        // back when it decides the level should actually change (prod TASK-157: on the panel tap; bringup: after
        // the banner + hold).
        private void OnLevelResolved()
        {
            var won = _model.CurrentPhase == GamePhase.Won;          // same authority ApplyResolution reads (phase live)

            // Durable win: persist the checkpoint at the MOMENT OF RESOLUTION, not on the "Next" tap — win, don't
            // tap, kill the app → relaunch resumes the next level (Start reads the store). The final line-up level
            // included (TASK-528): its next step is the pool, never a wrap, so nothing is erased by writing early.
            // ApplyResolution then only moves the in-memory progress, no re-write.
            if (won)
            {
                _pendingWin = CampaignCursor.AfterWin(_progress, _levelSource.Count, _pool);
                _store.Write(_pendingWin);
            }

            _resolutionFlow.Present(_model.Outcome.CurrentValue, ApplyResolution, _position.IsFinalLineupLevel);
        }

        // The actual transition. Reads phase live at transition time; nothing re-resolves during the hold, so
        // Won/Lost is stable.
        private void ApplyResolution()
        {
            if (_model.CurrentPhase != GamePhase.Won)
            {
                // LOSS → retry the same level from scratch (no in-level restore); progress unchanged.
                _controller.ReloadLevel(_position.LineupIndex);
                return;
            }

            // WIN → the checkpoint was written on resolve; here we only adopt it and navigate (no double write).
            SetProgress(_pendingWin);
        }

        /// <summary>
        /// <see cref="ICampaignRestart"/> — restart the current level for the play-screen Restart button
        /// (TASK-116, Q3). Reuses the reset-in-place reload at the current position (the same path <see cref="Start"/>
        /// and the dev jump use), so there is no second pointer. Neutral for the domain: does NOT call
        /// <c>Resolve(Lost)</c>, is not counted as a loss/attempt, and does not persist.
        /// </summary>
        public void ReloadCurrent() => _controller.ReloadLevel(_position.LineupIndex);

        private void SetProgress(CampaignProgress progress)
        {
            _progress = progress;
            _position = CampaignCursor.Resolve(_progress, _levelSource.Count, _pool);
            _controller.ReloadLevel(_position.LineupIndex);
        }

        private int ClampToLineup(int index)
        {
            if (index < 0)
                return 0;
            var last = _levelSource.Count - 1;
            return index > last ? last : index;
        }

#if GPIE_DEVELOPMENT
        /// <summary>
        /// Dev/QA cheat hook (TASK-011; consumed by the DDM hosts): the line-up index of the loaded level — on the
        /// pool track too, where it differs from <c>LevelNumber − 1</c>. Editor/dev builds only.
        /// </summary>
        public int DevCampaignIndex => _position.LineupIndex;

        /// <summary>Dev/QA (TASK-528): the in-memory progress, for "Level N · pool k/M" labels. Editor/dev only.</summary>
        public CampaignProgress DevProgress => _progress;

        /// <summary>Dev/QA (TASK-528): where the in-memory progress points (track, pool place). Editor/dev only.</summary>
        public CampaignPosition DevPosition => _position;

        /// <summary>Dev/QA (TASK-528): number of places in the pool circle. Editor/dev only.</summary>
        public int DevPoolCount => _pool.Count;

        /// <summary>
        /// Dev/QA cheat hook (TASK-011; consumed by the DDM host <c>BringupDebugMenuHost</c> since TASK-047):
        /// jump straight to line-up <paramref name="index"/> (first pass) and keep the in-memory progress in sync, so
        /// the next WIN/LOSS resolves relative to the jumped-to level. Clamped to the level range; reuses the normal
        /// reset-in-place reload path. Deliberately does NOT persist — the cheat is non-destructive to saved
        /// progress; a level actually resolved after the jump persists through the usual flow. Editor/dev only.
        /// </summary>
        public void DevJumpToLevel(int index)
        {
            SetProgress(CampaignCursor.AtLineup(ClampToLineup(index)));
        }

        /// <summary>
        /// Dev/QA (TASK-528): ◀/▶ WITHOUT persist (bringup — its jumps are non-destructive by design-lock, RFC Q2).
        /// Moves along the current track via <see cref="CampaignCursor.Step"/>: clamped on the line-up, around the
        /// circle in the pool. An unchanged progress is a silent no-op. Editor/dev only.
        /// </summary>
        public void DevStep(int delta)
        {
            var target = CampaignCursor.Step(_progress, delta, _levelSource.Count, _pool);
            if (target.Equals(_progress))
                return;
            SetProgress(target);
        }

        /// <summary>
        /// Dev/QA (TASK-528, owner acceptance without 80 wins): enter the pool at <paramref name="place"/>, numbered as
        /// if reached by playing. <paramref name="persist"/> = prod menu (writes the store, survives a relaunch);
        /// bringup passes false. Editor/dev only.
        /// </summary>
        public void DevJumpToPool(int place, bool persist)
        {
            var target = CampaignCursor.AtPool(place, _levelSource.Count);
            if (persist)
                _store.Write(target);
            SetProgress(target);
        }

        /// <summary>
        /// Dev/QA hook (TASK-113; consumed only by <c>GameDebugMenuRestartBridge</c>): re-read the progress from
        /// <see cref="ICampaignStore"/> and reload in place. This is the meaning of "restart" for the prod
        /// <c>GameDebugMenu</c> — NOT an Application relaunch. The prod menu's "Reset Progress" / "Go to Level"
        /// write <c>Data.Meta</c> to disk but never notified the in-memory owner, so only re-reading the store here
        /// makes them take effect — the next WIN/LOSS then resolves from the resynced progress. Editor/dev only.
        /// </summary>
        public void ResyncFromStore()
        {
            SetProgress(_store.Read());
        }

        /// <summary>
        /// Dev/QA hook (TASK-174; consumed only by <c>GameDebugMenuRestartBridge</c>): step by <paramref name="delta"/>
        /// (±1) WITH PERSIST — this backs the prod ◀/▶ arrows that page levels WITHOUT closing the debug menu. Same
        /// motion as <see cref="DevStep"/> (clamped on the line-up — an edge is a silent no-op, neither a re-write nor
        /// a reload; around the circle in the pool, TASK-528), then <see cref="ICampaignStore.Write"/> (durable, all
        /// keys coherently) and a reset-in-place reload. Editor/dev only.
        /// </summary>
        public void DevStepPersisted(int delta)
        {
            var target = CampaignCursor.Step(_progress, delta, _levelSource.Count, _pool);
            if (target.Equals(_progress))
                return;                       // line-up edge — no wrap, silent no-op
            _store.Write(target);
            SetProgress(target);
        }
#endif

        public void Dispose()
        {
            _resolvedSubscription?.Dispose();
            _resolvedSubscription = null;
        }
    }
}
