using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Gameplay.Core
{
    /// <summary>
    /// The replay pool after the line-up (TASK-528): an ordered circle of EXISTING line-up levels, authored as data in
    /// <c>shapefill_pool.json</c> by <c>levelId</c> and read through the same <see cref="ILevelStore"/> seam as the
    /// manifest. Levels are never copied — each id is translated once, here, into its line-up index.
    ///
    /// Never throws and never dead-ends: an unknown or duplicate id is SKIPPED and named in <see cref="Problems"/>; a
    /// missing, unparsable or (after filtering) empty file makes the pool the whole line-up (<see cref="IsFallback"/>).
    /// The fallback is not silent: the real files are gated by <c>LevelPoolDataTests</c> (Problems must be empty) and
    /// the runtime adapter logs every problem as an error on start. Kept in its own file, not in the manifest, so an
    /// edit of the pool does not trigger the post-merge sim sweep (the manifest is a sweep path; pool levels are not
    /// changed by the pool).
    /// </summary>
    public sealed class LevelPool
    {
        public const string PoolFileName = "shapefill_pool.json";

        private readonly List<int> _lineupIndices = new();
        private readonly List<string> _problems = new();

        public LevelPool(ILevelStore store, ILevelSource lineup)
        {
            Build(ReadIds(store), lineup);
        }

        private LevelPool()
        {
        }

        /// <summary>A pool from an explicit id list (tests, alternative sources); same filtering as the file path.</summary>
        public static LevelPool FromIds(IReadOnlyList<string> ids, ILevelSource lineup)
        {
            var pool = new LevelPool();
            pool.Build(ids, lineup);
            return pool;
        }

        /// <summary>Places in the circle; always ≥ 1 for a non-empty line-up.</summary>
        public int Count => _lineupIndices.Count;

        /// <summary>The pool is empty or broken and stands in for the whole line-up.</summary>
        public bool IsFallback { get; private set; }

        /// <summary>Human-readable defects of the source list; empty for a healthy pool.</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>Line-up index at circle place <paramref name="cursor"/>, taken modulo <see cref="Count"/>
        /// (negative wraps from the end), so a stale cursor never throws.</summary>
        public int LineupIndexAt(int cursor)
        {
            if (_lineupIndices.Count == 0)
                return 0;
            return _lineupIndices[Wrap(cursor, _lineupIndices.Count)];
        }

        /// <summary>Non-negative modulo — the one place the circle wraps.</summary>
        public static int Wrap(int value, int count)
        {
            var m = value % count;
            return m < 0 ? m + count : m;
        }

        private List<string> ReadIds(ILevelStore store)
        {
            var json = store.ReadText(PoolFileName);
            if (string.IsNullOrEmpty(json))
            {
                _problems.Add($"pool file '{PoolFileName}' is missing or empty");
                return null;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<LevelPoolFileDto>(json);
                if (file?.Pool == null)
                    _problems.Add($"pool file '{PoolFileName}' has no 'pool' array");
                return file?.Pool;
            }
            catch (JsonException e)
            {
                _problems.Add($"pool file '{PoolFileName}' does not parse: {e.Message}");
                return null;
            }
        }

        private void Build(IReadOnlyList<string> ids, ILevelSource lineup)
        {
            var indexById = new Dictionary<string, int>(lineup.Count);
            for (var i = 0; i < lineup.Count; i++)
            {
                var id = lineup.Load(i).LevelId;
                if (!string.IsNullOrEmpty(id) && !indexById.ContainsKey(id))
                    indexById.Add(id, i);
            }

            if (ids != null)
            {
                var seen = new HashSet<string>();
                for (var place = 0; place < ids.Count; place++)
                {
                    var id = ids[place];
                    if (!indexById.TryGetValue(id ?? string.Empty, out var lineupIndex))
                    {
                        _problems.Add($"pool[{place}] id '{id}' is not in the line-up manifest — skipped");
                        continue;
                    }

                    if (!seen.Add(id))
                    {
                        _problems.Add($"pool[{place}] id '{id}' is a duplicate — skipped");
                        continue;
                    }

                    _lineupIndices.Add(lineupIndex);
                }
            }

            if (_lineupIndices.Count > 0)
                return;

            // Empty after filtering (or no file at all) → the whole line-up, in order. Never a dead end.
            IsFallback = true;
            if (ids != null)
                _problems.Add("pool is empty after filtering — falling back to the whole line-up");
            for (var i = 0; i < lineup.Count; i++)
                _lineupIndices.Add(i);
        }
    }

    /// <summary>On-disk shape of <c>shapefill_pool.json</c> (Newtonsoft, stable field names like <see cref="CampaignFileDto"/>).</summary>
    internal sealed class LevelPoolFileDto
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty("pool")] public List<string> Pool { get; set; }
    }
}
