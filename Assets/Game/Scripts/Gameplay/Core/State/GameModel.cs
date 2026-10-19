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
        private readonly Subject<int> _pieceReleased = new();
        private readonly ReactiveProperty<bool> _fieldAtRest = new(false);
        private readonly List<PieceInstance> _pieces = new();
        private IReadOnlyList<WinFigurePiece> _winFigure = System.Array.Empty<WinFigurePiece>();

        public ReadOnlyReactiveProperty<GamePhase> Phase => _phase;
        public ReadOnlyReactiveProperty<GameOutcome> Outcome => _outcome;
        public Observable<Unit> LevelLoaded => _levelLoaded;
        public Observable<Unit> LevelResolved => _levelResolved;

        /// <summary>
        /// Emits the InstanceId of a piece the moment the player releases it (TASK-078 §2): the precise
        /// "highlighted piece actually left Idle" signal the step-by-step tutorial needs — a first tap alone
        /// (which can miss empty space) is not enough. Fires only on a player release (never on SpawnPreplaced),
        /// so pre-placed pieces stay silent. Unity-free <c>int</c>, same family as LevelLoaded/LevelResolved →
        /// the headless-simulatable invariant holds; the sim graph subscribes to nothing here, so it is a no-op.
        /// </summary>
        public Observable<int> PieceReleased => _pieceReleased;

        /// <summary>
        /// True while the whole field has come to GLOBAL rest and play continues — i.e. the moment the field is
        /// waiting for the player's next tap (TASK-524). Raised by the mechanic controller on the same global-rest
        /// gate that drives the verdict; dropped by every player release (<see cref="RegisterAttempt"/>, BEFORE
        /// <see cref="PieceReleased"/> emits, so an observer of the release never reads a stale "at rest") and by
        /// every level (re)load. The tutorial waits on it so a step never points at the next piece while the previous
        /// one is still falling. Observe-only, like <see cref="PieceReleased"/>: the sim graph subscribes to nothing
        /// here, so poseHash/level-sim are unaffected.
        /// </summary>
        public ReadOnlyReactiveProperty<bool> FieldAtRest => _fieldAtRest;

        /// <summary>
        /// The won figure (TASK-634): exactly the pieces the judge paired with target slots, with their landing
        /// angles. Set by the mechanic controller BEFORE <see cref="Resolve"/>(Won), so a synchronous subscriber of
        /// <see cref="Outcome"/> already reads it; empty otherwise and cleared on every (re)load. The win beat flies
        /// these pieces into the target card. Observe-only and transient (not in any snapshot): poseHash unaffected.
        /// </summary>
        public IReadOnlyList<WinFigurePiece> WinFigure => _winFigure;

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
            _fieldAtRest.Value = false;
            _winFigure = System.Array.Empty<WinFigurePiece>();

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

        /// <summary>Record a player release: bump the attempt counter and emit <see cref="PieceReleased"/> with
        /// the released instance. "Attempt" and "release" are the same event (the counter already ticks once per
        /// player release), so they share one call rather than a second method.</summary>
        public void RegisterAttempt(int instanceId)
        {
            AttemptsUsed++;
            _fieldAtRest.Value = false; // a release disturbs the field; drop BEFORE observers of the release run
            _pieceReleased.OnNext(instanceId);
        }

        /// <summary>Mechanic controller only: publish whether the field is at global rest with play continuing
        /// (see <see cref="FieldAtRest"/>).</summary>
        public void SetFieldAtRest(bool atRest) => _fieldAtRest.Value = atRest;

        /// <summary>Mechanic controller only: publish the won figure right before resolving Won (see
        /// <see cref="WinFigure"/>).</summary>
        public void SetWinFigure(IReadOnlyList<WinFigurePiece> figure) =>
            _winFigure = figure ?? System.Array.Empty<WinFigurePiece>();

        public void Resolve(GameOutcome outcome)
        {
            _outcome.Value = outcome;
            _phase.Value = outcome == GameOutcome.Won ? GamePhase.Won : GamePhase.Lost;
            _levelResolved.OnNext(Unit.Default);
        }
    }
}
