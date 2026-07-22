using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Fills the <c>IMechanicController</c> seam with the pivoted shape_fill loop (GAME_RULES §2–3), all pure
    /// C# (headless-testable via ScriptedPoseSimulation): all pieces are pre-placed Idle over their target
    /// slots; a tap hit-tests which Idle piece it hit and releases it; releasing materialises the body so it
    /// falls; poses are mirrored into PieceInstances; the Idle→Falling→Placed/Lost FSM runs; and on global
    /// rest a per-piece pose-match decides Won/Lost. Physics itself lives behind IShapeFillSimulation.
    /// </summary>
    public sealed class ShapeFillMechanicController : IMechanicController, IDisposable
    {
        private readonly GameModel _model;
        private readonly IShapeFillSimulation _simulation;
        private readonly IMatchEvaluator _match;
        private readonly LevelDataValidator _validator;
        private readonly IPieceViewFactory _viewFactory;
        private readonly ShapeFillTuning _tuning;

        private readonly Dictionary<int, int> _bodyByInstance = new();
        private readonly Dictionary<int, IPieceView> _viewByInstance = new();
        private readonly Dictionary<int, PieceType> _typeByInstance = new();
        private readonly List<PlacedPiece> _placedScratch = new();

        private IDisposable _levelLoadedSub;
        private IDisposable _stateRestoredSub;
        private LevelData _level;

        public ShapeFillMechanicController(
            GameModel model,
            IShapeFillSimulation simulation,
            IMatchEvaluator match,
            LevelDataValidator validator,
            IPieceViewFactory viewFactory,
            ShapeFillTuning tuning)
        {
            _model = model;
            _simulation = simulation;
            _match = match;
            _validator = validator;
            _viewFactory = viewFactory;
            _tuning = tuning;

            _levelLoadedSub = _model.LevelLoaded.Subscribe(_ => OnLevelLoaded());
            _stateRestoredSub = _model.StateRestored.Subscribe(_ => OnStateRestored());
        }

        private void OnLevelLoaded()
        {
            _level = _model.CurrentLevel;

            var validation = _validator.Validate(_level);
            if (!validation.Ok)
            {
                // Structural brak: refuse to run rather than present an unwinnable level.
                _model.Resolve(GameOutcome.Lost);
                return;
            }

            ClearRuntime();
            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            _model.SetPhase(GamePhase.Running);

            // Pre-place every piece Idle (floating, non-colliding) at its authored start pose (RFC §3/§5.3).
            foreach (var piece in _model.Pieces)
                SpawnIdle(piece);
        }

        /// <summary>
        /// Rebuild runtime state from a restored snapshot (RFC §8). Runs AFTER OnLevelLoaded, so it tears down
        /// the fresh spawn and re-materialises bodies/views from each piece's saved state: Idle → floating
        /// (non-colliding), Placed → settled (kinematic, supports others), Lost → skipped.
        /// </summary>
        private void OnStateRestored()
        {
            _level = _model.CurrentLevel;

            ClearRuntime();
            _simulation.Reset();
            _simulation.LoadStatics(_level.StaticGeometry, _level.Bounds, _level.Gravity);

            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost)
                    continue;

                var type = _model.TypeFor(piece);
                var bodyId = piece.State == PieceLifecycle.Placed
                    ? _simulation.SpawnSettled(type, piece.Position, piece.Rotation)
                    : _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

                _bodyByInstance[piece.InstanceId] = bodyId;
                _typeByInstance[piece.InstanceId] = type;

                var view = _viewFactory.Create(type);
                view.SetState(piece.State);
                view.Sync(piece.Position, piece.Rotation);
                _viewByInstance[piece.InstanceId] = view;
            }
        }

        public void OnIntent(InputIntent intent)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var hit = PickIdlePieceAt(new Vec2(intent.X, intent.Y));
            if (hit == null)
                return;

            _simulation.Release(_bodyByInstance[hit.InstanceId]);
            hit.State = PieceLifecycle.Falling;
            hit.RestTimer = 0f;
            _viewByInstance[hit.InstanceId].SetState(PieceLifecycle.Falling);
            _model.RegisterAttempt();
        }

        public void Tick(float dt)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var anyFalling = false;

            foreach (var piece in _model.Pieces)
            {
                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                if (piece.State == PieceLifecycle.Falling)
                {
                    UpdateFalling(piece, bodyId, dt);
                    if (piece.State == PieceLifecycle.Falling)
                        anyFalling = true;
                }

                // The body may have been despawned (Lost) inside UpdateFalling.
                if (_bodyByInstance.TryGetValue(piece.InstanceId, out bodyId))
                    MirrorPose(piece, bodyId);
            }

            if (anyFalling)
                return;

            ResolveAtRest();
        }

        private void UpdateFalling(PieceInstance piece, int bodyId, float dt)
        {
            if (!_simulation.IsWithinBounds(bodyId))
            {
                piece.State = PieceLifecycle.Lost;
                _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Lost);
                _simulation.Despawn(bodyId);
                _bodyByInstance.Remove(piece.InstanceId);
                return;
            }

            if (_simulation.GetSpeed(bodyId) < _tuning.RestSpeedEpsilon)
            {
                piece.RestTimer += dt;
                if (piece.RestTimer >= _tuning.RestDurationSeconds)
                {
                    piece.State = PieceLifecycle.Placed;
                    _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);
                }
            }
            else
            {
                piece.RestTimer = 0f;
            }
        }

        private void MirrorPose(PieceInstance piece, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);
            piece.Position = pose.Position;
            piece.Rotation = pose.Rotation;
            _viewByInstance[piece.InstanceId].Sync(pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Verdict at global rest (RFC §4.3). Won when every slot is filled by a pose-matched piece and none
        /// is Lost. If no piece is Idle or Falling (nothing left to change) and it is not Won → Lost. Otherwise
        /// Idle pieces remain → wait for the player's next tap (no instant fail; canon §3 judges at final rest).
        /// </summary>
        private void ResolveAtRest()
        {
            var result = _match.Evaluate(_level.Target, CollectPlaced(), _level.MatchTolerance);

            var anyLost = false;
            var anyIdle = false;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost) anyLost = true;
                else if (piece.State == PieceLifecycle.Idle) anyIdle = true;
            }

            if (result.AllSlotsFilled && !anyLost)
            {
                _model.Resolve(GameOutcome.Won);
                return;
            }

            if (anyIdle)
                return; // pieces remain on the field — wait for the player's tap.

            // Nothing Idle or Falling and the figure is not complete → the level cannot progress further.
            _model.Resolve(GameOutcome.Lost);
        }

        private IReadOnlyList<PlacedPiece> CollectPlaced()
        {
            _placedScratch.Clear();
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                var type = _typeByInstance[piece.InstanceId];
                _placedScratch.Add(new PlacedPiece(piece.Kind, piece.Position, piece.Rotation, type.SymmetryOrder));
            }
            return _placedScratch;
        }

        /// <summary>Nearest Idle piece whose shape (at its current pose) contains the tapped point (RFC §5.2).</summary>
        private PieceInstance PickIdlePieceAt(Vec2 p)
        {
            PieceInstance best = null;
            var bestSqr = float.MaxValue;

            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Idle || !_bodyByInstance.ContainsKey(piece.InstanceId))
                    continue;

                var type = _typeByInstance[piece.InstanceId];
                var world = Geometry.WorldVertices(type.Shape, piece.Position, piece.Rotation);
                if (!Geometry.PointInPolygon(world, p))
                    continue;

                var sqr = (piece.Position - p).SqrLength; // tie-break by centroid distance (determinism)
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = piece;
                }
            }

            return best;
        }

        private void SpawnIdle(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;

            var view = _viewFactory.Create(type);
            view.SetState(PieceLifecycle.Idle);
            view.Sync(piece.Position, piece.Rotation);
            _viewByInstance[piece.InstanceId] = view;
        }

        private void ClearRuntime()
        {
            foreach (var view in _viewByInstance.Values)
                view.Dispose();
            _bodyByInstance.Clear();
            _viewByInstance.Clear();
            _typeByInstance.Clear();
        }

        public void Dispose()
        {
            _levelLoadedSub?.Dispose();
            _levelLoadedSub = null;
            _stateRestoredSub?.Dispose();
            _stateRestoredSub = null;
        }
    }
}
