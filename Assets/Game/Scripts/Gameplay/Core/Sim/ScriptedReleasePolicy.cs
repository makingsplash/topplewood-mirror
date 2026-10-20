using System;
using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// One authored release entry (TASK-060): tap the Idle piece(s) at <see cref="PlacementIndices"/> and release
    /// them together, <see cref="DelaySteps"/> fixed steps AFTER the previous entry's release (the first entry is
    /// relative to the run start, so <c>0</c> = release on step 0). Timing is in FIXED STEPS, never wall-clock, so
    /// a script is reproducible byte-for-byte. A single-element <see cref="PlacementIndices"/> is the ordinary
    /// "one piece" case; a multi-element array is a synchronous group (a symmetric pair) whose taps all enter the
    /// same physics step, exactly like <see cref="PairReleasePolicy"/>'s group-flush.
    /// </summary>
    public readonly struct ScriptedStep
    {
        public readonly int[] PlacementIndices;
        public readonly int DelaySteps;

        public ScriptedStep(int[] placementIndices, int delaySteps)
        {
            PlacementIndices = placementIndices;
            DelaySteps = delaySteps;
        }
    }

    /// <summary>
    /// Expressibility primitive (TASK-060): lets the sim play like a HUMAN — choose which pieces to release AND
    /// WHEN, including tapping the next piece while the previous one is still falling ("impatient" play). It is the
    /// only policy that is NOT rest-gated (<see cref="GatedByRest"/> == false), so the driver asks it on EVERY
    /// Running step; the policy itself throttles by counting fixed steps against the authored script. This does NOT
    /// weaken the game's rules: it only PUSHES a tap the same way a fast-tapping player would — the release still
    /// runs through the unchanged input seam (<c>SimBotInputSource → GameController.OnIntent</c>), so the game
    /// decides the release under its own rules. What changes is only WHEN the driver offers the tap.
    ///
    /// Determinism: the driver calls a non-gated policy exactly once per Running step, so the policy's own call
    /// counter equals the driver's step index (both start at 0 on the first <see cref="SimRun.Step"/>, the phase is
    /// already Running after <c>controller.Start()</c> in the ctor). Absolute due steps are the cumulative sum of
    /// the script's <see cref="ScriptedStep.DelaySteps"/>, computed once in the ctor — no RNG, no wall-clock — so
    /// the same script yields the same poses over any number of repeats.
    /// </summary>
    public sealed class ScriptedReleasePolicy : ISimBotPolicy
    {
        private readonly ScriptedStep[] _script;
        // Absolute step at which each entry fires: cumulative sum of DelaySteps. An entry is consumed the first
        // step _step reaches its due value regardless of whether its pieces are still Idle, so the release step is
        // ALWAYS the due step — timing is independent of idle-state, keeping the script byte-for-byte reproducible.
        private readonly int[] _dueStep;
        // TASK-649/650: a tap an active piece caught releases nothing, and a player would simply tap again. Every
        // tapped piece (outside a co-release group) is re-checked _retrySteps later; still Idle → tapped again at its
        // CURRENT tap point, and again every _retrySteps until it falls or the level ends. Only a caught tap leaves a
        // non-grouped piece Idle, so a run without one never re-taps (byte-for-byte the old script). 0 = no repeat.
        private readonly int _retrySteps;
        private readonly List<(int PlacementIndex, int DueStep)> _retries = new();
        private int _step;
        private int _cursor;

        public ScriptedReleasePolicy(ScriptedStep[] script, int retrySteps = 0)
        {
            _retrySteps = retrySteps;
            _script = script ?? Array.Empty<ScriptedStep>();
            _dueStep = new int[_script.Length];
            var acc = 0;
            for (var i = 0; i < _script.Length; i++)
            {
                acc += _script[i].DelaySteps;
                _dueStep[i] = acc;
            }
        }

        // The whole point of this policy: it drives its OWN timing by step count, so the driver must not gate it on
        // field rest. Every other policy returns true and keeps its byte-for-byte "wait for rest" behaviour.
        public bool GatedByRest => false;

        public bool TryNextRelease(GameModel model, List<Vec2> taps)
        {
            taps.Clear();
            // Repeat the caught taps that are due (oldest first — a player re-taps before moving on); a piece that
            // has fallen since leaves the list.
            for (var i = _retries.Count - 1; i >= 0; i--)
                if (FindIdle(model, _retries[i].PlacementIndex) == null)
                    _retries.RemoveAt(i);
            for (var i = 0; i < _retries.Count; i++)
            {
                var (placementIndex, due) = _retries[i];
                if (_step < due)
                    continue;
                taps.Add(SimGeometry.TapPoint(model, FindIdle(model, placementIndex)));
                _retries[i] = (placementIndex, _step + _retrySteps);
            }

            // Drain every entry due at this step (a later entry with DelaySteps == 0 fires in the same step as its
            // predecessor — a scripted synchronous group), tapping whichever addressed pieces are still Idle.
            while (_cursor < _script.Length && _step >= _dueStep[_cursor])
            {
                foreach (var placementIndex in _script[_cursor].PlacementIndices)
                {
                    var piece = FindIdle(model, placementIndex);
                    if (piece == null)
                        continue;
                    taps.Add(SimGeometry.TapPoint(model, piece));
                    ScheduleRetry(model, placementIndex);
                }
                _cursor++;
            }

            _step++;
            return taps.Count > 0;
        }

        /// <summary>Re-check this tap <see cref="_retrySteps"/> later (see the field). A co-release group member is
        /// left alone: its lone tap legitimately leaves it Idle (the group waits for its partners), so a repeat would
        /// re-open the coincidence window — a different gesture, not "the same tap again".</summary>
        private void ScheduleRetry(GameModel model, int placementIndex)
        {
            if (_retrySteps <= 0 || InCoReleaseGroup(model.CurrentLevel, placementIndex))
                return;
            for (var i = 0; i < _retries.Count; i++)
                if (_retries[i].PlacementIndex == placementIndex)
                {
                    _retries[i] = (placementIndex, _step + _retrySteps);
                    return;
                }
            _retries.Add((placementIndex, _step + _retrySteps));
        }

        private static bool InCoReleaseGroup(LevelData level, int placementIndex)
        {
            if (level?.CoReleaseGroups == null)
                return false;
            foreach (var group in level.CoReleaseGroups)
                foreach (var member in group)
                    if (member == placementIndex)
                        return true;
            return false;
        }

        /// <summary>
        /// Named "impatient player" profile (TASK-060): release the pieces in <paramref name="order"/> with the
        /// first on step 0 and each subsequent one <paramref name="gapSteps"/> fixed steps later — WITHOUT waiting
        /// for the field to settle. Reuses the exact placement-index orders <see cref="SolveSweepPlan"/> already
        /// builds, so it adds no new orders to the sweep space; it only removes the rest-gate. TASK-650: a caught tap
        /// is repeated at the same tempo, <paramref name="gapSteps"/> later.
        /// </summary>
        public static ScriptedReleasePolicy Impatient(int[] order, int gapSteps)
        {
            var script = new ScriptedStep[order.Length];
            for (var i = 0; i < order.Length; i++)
                script[i] = new ScriptedStep(new[] { order[i] }, i == 0 ? 0 : gapSteps);
            return new ScriptedReleasePolicy(script, gapSteps);
        }

        private static PieceInstance FindIdle(GameModel model, int placementIndex)
        {
            foreach (var piece in model.Pieces)
            {
                if (piece.PlacementIndex == placementIndex && piece.State == PieceLifecycle.Idle)
                    return piece;
            }
            return null;
        }
    }
}
