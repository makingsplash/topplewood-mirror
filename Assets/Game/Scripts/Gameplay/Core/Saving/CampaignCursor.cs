using System;

namespace Gameplay.Core
{
    /// <summary>Which part of the campaign a <see cref="CampaignProgress"/> points into (TASK-528).</summary>
    public enum CampaignTrack
    {
        /// <summary>First pass of the line-up (<c>LineupCleared == 0</c>).</summary>
        Lineup,

        /// <summary>The line-up grew after the player reached the pool: its new tail is played before returning.</summary>
        CatchUp,

        /// <summary>The replay circle of <see cref="LevelPool"/>.</summary>
        Pool,
    }

    /// <summary>What to load for a given progress: the line-up index plus where it came from.</summary>
    public readonly struct CampaignPosition
    {
        public readonly int LineupIndex;
        public readonly CampaignTrack Track;

        /// <summary>The last level of the FIRST pass — the only one that shows "ALL DONE!" (once per game).</summary>
        public readonly bool IsFinalLineupLevel;

        /// <summary>Place in the pool circle (0-based), or -1 off the pool.</summary>
        public readonly int PoolPlace;

        public CampaignPosition(int lineupIndex, CampaignTrack track, bool isFinalLineupLevel, int poolPlace)
        {
            LineupIndex = lineupIndex;
            Track = track;
            IsFinalLineupLevel = isFinalLineupLevel;
            PoolPlace = poolPlace;
        }
    }

    /// <summary>
    /// Pure campaign transitions (TASK-528, architect 03 §3.3 — the rule table IS the spec, <c>CampaignCursorTests</c>
    /// follow it row by row). Unity-free and stateless so every transition is provable in EditMode:
    /// <list type="table">
    /// <item><b>A</b> <c>LineupCleared == 0</c>: load <c>clamp(LevelNumber−1)</c>; a win advances the number; a win on
    /// the LAST line-up level enters the pool (<c>LineupCleared = Count</c>, <c>PoolCursor = 0</c>) — no wrap to 1.</item>
    /// <item><b>B1</b> <c>0 &lt; LineupCleared &lt; Count</c> (the line-up grew): load <c>LineupCleared</c>; a win
    /// advances it; the saved pool place is kept, so the player returns to the same spot in the circle.</item>
    /// <item><b>B2</b> <c>LineupCleared ≥ Count</c>: load <c>pool[PoolCursor]</c>; a win moves to the next place,
    /// wrapping around the circle. A shrunken line-up (<c>LineupCleared &gt; Count</c>) stays here.</item>
    /// </list>
    /// A loss never changes progress. The shown number only grows. <c>LineupCleared</c> is positional: correct while
    /// the line-up grows by appending (see <c>docs/architecture/level-order.md</c> «Пул после линейки»).
    /// </summary>
    public static class CampaignCursor
    {
        public static CampaignPosition Resolve(CampaignProgress p, int lineupCount, LevelPool pool)
        {
            var count = Math.Max(1, lineupCount);

            if (p.LineupCleared <= 0)
            {
                var index = Clamp(p.LevelNumber - 1, 0, count - 1);
                return new CampaignPosition(index, CampaignTrack.Lineup, index == count - 1, -1);
            }

            if (p.LineupCleared < count)
                return new CampaignPosition(p.LineupCleared, CampaignTrack.CatchUp, false, -1);

            var place = LevelPool.Wrap(p.PoolCursor, pool.Count);
            return new CampaignPosition(pool.LineupIndexAt(place), CampaignTrack.Pool, false, place);
        }

        public static CampaignProgress AfterWin(CampaignProgress p, int lineupCount, LevelPool pool)
        {
            var count = Math.Max(1, lineupCount);
            var position = Resolve(p, count, pool);

            switch (position.Track)
            {
                case CampaignTrack.Lineup:
                    // Max(): a stale/hand-edited number below its own index must still move forward, never stall.
                    var next = Math.Max(p.LevelNumber, position.LineupIndex + 1) + 1;
                    return position.IsFinalLineupLevel
                        ? new CampaignProgress(next, count, 0)   // the line-up is done → the pool, from place 0
                        : new CampaignProgress(next, 0, 0);
                case CampaignTrack.CatchUp:
                    return new CampaignProgress(p.LevelNumber + 1, p.LineupCleared + 1, p.PoolCursor);
                default:
                    return new CampaignProgress(
                        p.LevelNumber + 1, p.LineupCleared, LevelPool.Wrap(position.PoolPlace + 1, pool.Count));
            }
        }

        /// <summary>
        /// Dev ◀/▶ (<paramref name="delta"/> = ±1). A: clamped to the line-up, no wrap (an edge returns the same
        /// progress — a silent no-op). B1: <c>LineupCleared</c> within [1, Count]; reaching Count enters the pool.
        /// B2: around the circle, the number moves by delta but never below <c>Count + 1</c>.
        /// </summary>
        public static CampaignProgress Step(CampaignProgress p, int delta, int lineupCount, LevelPool pool)
        {
            var count = Math.Max(1, lineupCount);
            var position = Resolve(p, count, pool);

            switch (position.Track)
            {
                case CampaignTrack.Lineup:
                    var target = Clamp(position.LineupIndex + delta, 0, count - 1);
                    return target == position.LineupIndex ? p : AtLineup(target);
                case CampaignTrack.CatchUp:
                    var cleared = Clamp(p.LineupCleared + delta, 1, count);
                    return cleared == p.LineupCleared
                        ? p
                        : new CampaignProgress(p.LevelNumber + (cleared - p.LineupCleared), cleared, p.PoolCursor);
                default:
                    return new CampaignProgress(
                        Math.Max(count + 1, p.LevelNumber + delta), p.LineupCleared,
                        LevelPool.Wrap(position.PoolPlace + delta, pool.Count));
            }
        }

        /// <summary>Dev "Go to Level": line-up index <paramref name="index"/> of the first pass (the caller clamps).</summary>
        public static CampaignProgress AtLineup(int index) => new(index + 1, 0, 0);

        /// <summary>Dev "Jump to pool": place <paramref name="cursor"/>, numbered as if reached by playing (Count+1+cursor).</summary>
        public static CampaignProgress AtPool(int cursor, int lineupCount)
        {
            var count = Math.Max(1, lineupCount);
            return new CampaignProgress(count + 1 + Math.Max(0, cursor), count, cursor);
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
