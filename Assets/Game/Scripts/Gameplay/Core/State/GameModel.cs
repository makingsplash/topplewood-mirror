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
        private readonly List<PieceInstance> _pieces = new();

        public ReadOnlyReactiveProperty<GamePhase> Phase => _phase;
        public ReadOnlyReactiveProperty<GameOutcome> Outcome => _outcome;
        public Observable<Unit> LevelLoaded => _levelLoaded;
        public Observable<Unit> LevelResolved => _levelResolved;

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
    }
}
