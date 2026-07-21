using System.Collections.Generic;
using R3;

namespace Gameplay.Core
{
    /// <summary>
    /// Pure R3 session state (overview §Стейт-машина): reactive phase/outcome plus the current level,
    /// its block instances, pool cursor and attempts. No Unity types, no tick logic — the mechanic
    /// controller mutates it; views/analytics observe it.
    /// </summary>
    public sealed class GameModel
    {
        private readonly ReactiveProperty<GamePhase> _phase = new(GamePhase.Loading);
        private readonly ReactiveProperty<GameOutcome> _outcome = new(GameOutcome.None);
        private readonly Subject<Unit> _levelLoaded = new();
        private readonly Subject<Unit> _levelResolved = new();
        private readonly List<BlockInstance> _blocks = new();

        public ReadOnlyReactiveProperty<GamePhase> Phase => _phase;
        public ReadOnlyReactiveProperty<GameOutcome> Outcome => _outcome;
        public Observable<Unit> LevelLoaded => _levelLoaded;
        public Observable<Unit> LevelResolved => _levelResolved;

        public LevelData CurrentLevel { get; private set; }
        public IReadOnlyList<BlockInstance> Blocks => _blocks;
        public int PoolCursor { get; private set; }
        public int AttemptsUsed { get; private set; }

        public GamePhase CurrentPhase => _phase.Value;

        public void SetPhase(GamePhase phase) => _phase.Value = phase;

        /// <summary>Build fresh runtime state from a level: block instances (deterministic ids), cursor 0.</summary>
        public void LoadLevel(LevelData level)
        {
            CurrentLevel = level;
            _blocks.Clear();
            PoolCursor = 0;
            AttemptsUsed = 0;
            _outcome.Value = GameOutcome.None;

            var instanceId = level.Seed * 1000;
            var poolIndex = 0;
            foreach (var def in level.BlockPool)
            {
                for (var c = 0; c < def.Count; c++)
                    _blocks.Add(new BlockInstance(instanceId++, poolIndex, def.Kind));
                poolIndex++;
            }

            _phase.Value = GamePhase.Ready;
            _levelLoaded.OnNext(Unit.Default);
        }

        /// <summary>The pool block definition backing a given instance.</summary>
        public BlockDef DefFor(BlockInstance instance) => CurrentLevel.BlockPool[instance.PoolIndex];

        public BlockInstance NextPooledBlock()
        {
            if (PoolCursor >= _blocks.Count)
                return null;
            return _blocks[PoolCursor++];
        }

        public bool PoolExhausted => PoolCursor >= _blocks.Count;

        public void RegisterAttempt() => AttemptsUsed++;

        public void Resolve(GameOutcome outcome)
        {
            _outcome.Value = outcome;
            _phase.Value = outcome == GameOutcome.Won ? GamePhase.Won : GamePhase.Lost;
            _levelResolved.OnNext(Unit.Default);
        }

        public IGameSnapshot CaptureState()
        {
            var snapshot = new GameSnapshot
            {
                LevelId = CurrentLevel?.LevelId,
                PoolCursor = PoolCursor,
                Phase = _phase.Value,
                AttemptsUsed = AttemptsUsed,
            };

            foreach (var block in _blocks)
            {
                snapshot.Blocks.Add(new BlockSnapshot
                {
                    InstanceId = block.InstanceId,
                    PoolIndex = block.PoolIndex,
                    Kind = block.Kind,
                    State = block.State,
                    PosX = block.Position.X,
                    PosY = block.Position.Y,
                    Rot = block.Rotation,
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Restore a v2 snapshot onto the already-loaded level. Older/foreign schema is ignored
        /// (forward migration = fresh level), matching the forgiving design and RFC §6.
        /// </summary>
        public bool RestoreState(IGameSnapshot snapshot)
        {
            if (snapshot is not GameSnapshot v2 || v2.SchemaVersion != GameSnapshot.CurrentSchemaVersion)
                return false;
            if (CurrentLevel == null || v2.LevelId != CurrentLevel.LevelId)
                return false;

            PoolCursor = v2.PoolCursor;
            AttemptsUsed = v2.AttemptsUsed;

            var byId = new Dictionary<int, BlockSnapshot>(v2.Blocks.Count);
            foreach (var b in v2.Blocks)
                byId[b.InstanceId] = b;

            foreach (var block in _blocks)
            {
                if (!byId.TryGetValue(block.InstanceId, out var saved))
                    continue;
                // Any block persisted mid-fall returns to Floating (save-at-rest policy, RFC §6).
                block.State = saved.State == BlockLifecycle.Falling ? BlockLifecycle.Floating : saved.State;
                block.Position = new Vec2(saved.PosX, saved.PosY);
                block.Rotation = saved.Rot;
                block.RestTimer = 0f;
            }

            _phase.Value = v2.Phase;
            return true;
        }
    }
}
