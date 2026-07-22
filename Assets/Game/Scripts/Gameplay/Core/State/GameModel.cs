using System.Collections.Generic;
using R3;

namespace Gameplay.Core
{
    /// <summary>
    /// Pure R3 session state (overview §Стейт-машина): reactive phase/outcome plus the current level, its
    /// pre-placed piece instances and release count. No Unity types, no tick logic — the mechanic controller
    /// mutates it; views/analytics observe it. TASK-007 pivot: pre-placed pieces, no pool cursor.
    /// </summary>
    public sealed class GameModel
    {
        private readonly ReactiveProperty<GamePhase> _phase = new(GamePhase.Loading);
        private readonly ReactiveProperty<GameOutcome> _outcome = new(GameOutcome.None);
        private readonly Subject<Unit> _levelLoaded = new();
        private readonly Subject<Unit> _levelResolved = new();
        private readonly Subject<Unit> _stateRestored = new();
        private readonly List<PieceInstance> _pieces = new();

        public ReadOnlyReactiveProperty<GamePhase> Phase => _phase;
        public ReadOnlyReactiveProperty<GameOutcome> Outcome => _outcome;
        public Observable<Unit> LevelLoaded => _levelLoaded;
        public Observable<Unit> LevelResolved => _levelResolved;

        /// <summary>Fires after a snapshot is restored so the mechanic can rebuild bodies/views (RFC §8).</summary>
        public Observable<Unit> StateRestored => _stateRestored;

        public LevelData CurrentLevel { get; private set; }
        public IReadOnlyList<PieceInstance> Pieces => _pieces;

        /// <summary>Number of pieces released so far (analytics counter; not a fail limit).</summary>
        public int AttemptsUsed { get; private set; }

        public GamePhase CurrentPhase => _phase.Value;

        public void SetPhase(GamePhase phase) => _phase.Value = phase;

        /// <summary>
        /// Build fresh runtime state from a level: one Idle <see cref="PieceInstance"/> per placement, seeded
        /// at its authored start pose (the domain knows where each piece hangs — needed for hit-test before
        /// the first pose mirror). InstanceId is deterministic (<c>seed*1000 + placementIndex</c>).
        /// </summary>
        public void LoadLevel(LevelData level)
        {
            CurrentLevel = level;
            _pieces.Clear();
            AttemptsUsed = 0;
            _outcome.Value = GameOutcome.None;

            var id = level.Seed * 1000;
            for (var i = 0; i < level.Pieces.Count; i++)
            {
                var placement = level.Pieces[i];
                _pieces.Add(new PieceInstance(id + i, i, placement.Type.Kind, placement.StartPosition, placement.StartRotation));
            }

            _phase.Value = GamePhase.Ready;
            _levelLoaded.OnNext(Unit.Default);
        }

        /// <summary>The placement type backing a given instance.</summary>
        public PieceType TypeFor(PieceInstance instance) => CurrentLevel.Pieces[instance.PlacementIndex].Type;

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
                Phase = _phase.Value,
                AttemptsUsed = AttemptsUsed,
            };

            foreach (var piece in _pieces)
            {
                snapshot.Pieces.Add(new PieceSnapshot
                {
                    InstanceId = piece.InstanceId,
                    PlacementIndex = piece.PlacementIndex,
                    Kind = piece.Kind,
                    State = piece.State,
                    PosX = piece.Position.X,
                    PosY = piece.Position.Y,
                    Rot = piece.Rotation,
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Restore a v3 snapshot onto the already-loaded level. Older/foreign schema is ignored (forward
        /// migration = fresh level), matching the forgiving design and RFC §8.
        /// </summary>
        public bool RestoreState(IGameSnapshot snapshot)
        {
            if (snapshot is not GameSnapshot v3 || v3.SchemaVersion != GameSnapshot.CurrentSchemaVersion)
                return false;
            if (CurrentLevel == null || v3.LevelId != CurrentLevel.LevelId)
                return false;

            AttemptsUsed = v3.AttemptsUsed;

            var byId = new Dictionary<int, PieceSnapshot>(v3.Pieces.Count);
            foreach (var p in v3.Pieces)
                byId[p.InstanceId] = p;

            foreach (var piece in _pieces)
            {
                if (!byId.TryGetValue(piece.InstanceId, out var saved))
                    continue;
                // A piece persisted mid-fall returns to Idle (save-at-rest policy, RFC §8).
                piece.State = saved.State == PieceLifecycle.Falling ? PieceLifecycle.Idle : saved.State;
                piece.Position = new Vec2(saved.PosX, saved.PosY);
                piece.Rotation = saved.Rot;
                piece.RestTimer = 0f;
            }

            _phase.Value = v3.Phase;
            _stateRestored.OnNext(Unit.Default);
            return true;
        }
    }
}
