using System;

namespace Gameplay.Core
{
    /// <summary>
    /// The persisted campaign state (TASK-528): what <see cref="ICampaignStore"/> reads and writes. Three numbers, so
    /// the shown level number can keep growing after the line-up while the content cursor wraps around the pool —
    /// the "two counters" pattern. Interpretation lives in <see cref="CampaignCursor"/>.
    /// </summary>
    public readonly struct CampaignProgress : IEquatable<CampaignProgress>
    {
        /// <summary>1-based, monotonically growing: "Level N", the ×2 button, ad pacing. Prod = <c>Meta.CurrentLevel</c>.</summary>
        public readonly int LevelNumber;

        /// <summary>0 = first pass of the line-up; N &gt; 0 = N line-up positions cleared (the line-up was completed).</summary>
        public readonly int LineupCleared;

        /// <summary>Next place in the pool circle, 0-based (wrapped modulo the pool length when read).</summary>
        public readonly int PoolCursor;

        public CampaignProgress(int levelNumber, int lineupCleared, int poolCursor)
        {
            LevelNumber = levelNumber;
            LineupCleared = lineupCleared;
            PoolCursor = poolCursor;
        }

        public static CampaignProgress Fresh => new(1, 0, 0);

        /// <summary>The line-up was completed at least once (closes TASK-158: the fact used to be lost on the wrap).</summary>
        public bool CampaignCompleted => LineupCleared > 0;

        public bool Equals(CampaignProgress other) =>
            LevelNumber == other.LevelNumber && LineupCleared == other.LineupCleared && PoolCursor == other.PoolCursor;

        public override bool Equals(object obj) => obj is CampaignProgress other && Equals(other);

        public override int GetHashCode() => (LevelNumber * 397 ^ LineupCleared) * 397 ^ PoolCursor;

        public override string ToString() => $"Level {LevelNumber} · cleared {LineupCleared} · pool {PoolCursor}";
    }
}
