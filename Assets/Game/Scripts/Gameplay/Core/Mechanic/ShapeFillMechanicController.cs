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
        // Slots already claimed by a Placed piece this session — one slot, one piece (mirrors the evaluator's
        // greedy assignment) so the snap assist never sends two pieces to the same slot (RFC 07 §2.2).
        private readonly HashSet<int> _occupiedSlots = new();
        // Pieces gliding from their landing pose to a within-tolerance target (soft-settle, game-balancer 13),
        // keyed by InstanceId. While this is non-empty the rest verdict is deferred so the figure is never
        // judged mid-glide (§6.4).
        private readonly Dictionary<int, Settle> _settling = new();
        private readonly List<int> _settleDone = new();

        private IDisposable _levelLoadedSub;
        private IDisposable _stateRestoredSub;
        private LevelData _level;

        /// <summary>In-flight soft-settle glide for one piece: it is driven kinematically from Start to Target
        /// over TotalSteps fixed steps, then frozen at Target (game-balancer 13 §1/§6).</summary>
        private sealed class Settle
        {
            public int BodyId;
            public Vec2 StartPos;
            public float StartRot;
            public Vec2 TargetPos;
            public float TargetRot;
            public int Step;
            public int TotalSteps;
        }

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

            AdvanceSettling();

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

                // The body may have been despawned (Lost) inside UpdateFalling. Settling pieces are already
                // Placed, so MirrorPose just tracks the view along the kinematic glide set in AdvanceSettling.
                if (_bodyByInstance.TryGetValue(piece.InstanceId, out bodyId))
                    MirrorPose(piece, bodyId);
            }

            // Defer the verdict while any piece is still falling OR gliding (a piece that BeganSettle this tick
            // is added to _settling, so the same-tick guard also covers freshly-captured pieces).
            if (anyFalling || _settling.Count > 0)
                return;

            ResolveAtRest();
        }

        /// <summary>
        /// Advance every in-flight soft-settle glide by one fixed step: drive the (kinematic) body along the
        /// eased path from its landing pose to the within-tolerance target, then freeze it exactly at the target
        /// on the final step. Duration is counted in fixed steps, so the glide is headless-deterministic
        /// (game-balancer 13 §6.1); the angular path is the symmetry-reduced shortest arc (§6.2).
        /// </summary>
        private void AdvanceSettling()
        {
            if (_settling.Count == 0)
                return;

            _settleDone.Clear();
            foreach (var kv in _settling)
            {
                var s = kv.Value;
                s.Step++;

                Vec2 pos;
                float rot;
                if (s.Step >= s.TotalSteps)
                {
                    pos = s.TargetPos;
                    rot = s.TargetRot;
                    _settleDone.Add(kv.Key);
                }
                else
                {
                    var e = Ease(_tuning.SettleEasing, (float)s.Step / s.TotalSteps);
                    pos = s.StartPos + (s.TargetPos - s.StartPos) * e;
                    rot = s.StartRot + (s.TargetRot - s.StartRot) * e;
                }

                _simulation.FreezeAt(s.BodyId, pos, rot);
            }

            foreach (var instanceId in _settleDone)
                _settling.Remove(instanceId);
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
                    Place(piece, bodyId);
            }
            else
            {
                piece.RestTimer = 0f;
            }
        }

        /// <summary>
        /// Settle a piece at rest. When it landed within the capture window of a free slot (casual assist), begin
        /// a short eased glide onto a within-tolerance pose (soft-settle — reads as the piece easing into place,
        /// not a magnet snapping it; game-balancer 13). Otherwise freeze it exactly where it lies. Freezing
        /// (kinematic) kills post-rest drift and makes the piece a rigid support the next drop cannot shove — RFC 07 §2.1.
        /// </summary>
        private void Place(PieceInstance piece, int bodyId)
        {
            var slot = FindSnapSlot(piece, bodyId);
            if (slot >= 0)
            {
                BeginSettle(piece, bodyId, slot);
                return;
            }

            // No capture: nothing to soften — freeze exactly where it lies.
            var pose = _simulation.GetPose(bodyId);
            FreezePlaced(piece, pose.Position, pose.Rotation);
            _simulation.FreezeAt(bodyId, pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Kick off the soft-settle glide for a captured piece: claim the slot, kinematic-lock the body at its
        /// landing pose right now (so contacts can't drift it before the glide), mark the piece Placed (out of the
        /// Falling branch — the verdict is deferred while _settling is non-empty), and record the eased path to a
        /// within-tolerance target. game-balancer 13 §1/§6.
        /// </summary>
        private void BeginSettle(PieceInstance piece, int bodyId, int slot)
        {
            _occupiedSlots.Add(slot);

            var target = _level.Target.Slots[slot];
            var landed = _simulation.GetPose(bodyId);
            var symmetryOrder = _typeByInstance[piece.InstanceId].SymmetryOrder;

            _simulation.FreezeAt(bodyId, landed.Position, landed.Rotation);

            piece.State = PieceLifecycle.Placed;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);

            var steps = (int)MathF.Round(_tuning.SettleDurationSeconds / _tuning.FixedStep);
            if (steps < 1)
                steps = 1;

            _settling[piece.InstanceId] = new Settle
            {
                BodyId = bodyId,
                StartPos = landed.Position,
                StartRot = landed.Rotation,
                TargetPos = SettleTargetPosition(landed.Position, target.Position),
                TargetRot = SettleTargetRotation(landed.Rotation, target.Rotation, symmetryOrder),
                Step = 0,
                TotalSteps = steps,
            };
        }

        /// <summary>Freeze a piece Placed at an exact pose (no glide) and mirror it into the model + view.</summary>
        private void FreezePlaced(PieceInstance piece, Vec2 pos, float rot)
        {
            piece.State = PieceLifecycle.Placed;
            piece.Position = pos;
            piece.Rotation = rot;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);
            _viewByInstance[piece.InstanceId].Sync(pos, rot);
        }

        /// <summary>
        /// Settle target position: move a <c>SettleGain</c> fraction of the way from the landing pose toward the
        /// slot, leaving a (1−gain) residual. The residual is additionally clamped to
        /// <c>SettleMaxResidualFactor·posTol</c> so it never approaches the tolerance border (game-balancer 13 §2.3/§2.4).
        /// </summary>
        private Vec2 SettleTargetPosition(Vec2 landed, Vec2 slot)
        {
            var error = slot - landed;
            var dist = error.Length;
            if (dist <= 1e-6f)
                return slot;

            var maxResidual = _tuning.SettleMaxResidualFactor * _level.MatchTolerance.PosTol;
            var gain = EffectiveGain(dist, maxResidual);
            return landed + error * gain;
        }

        /// <summary>
        /// Settle target rotation: rotate a <c>SettleGain</c> fraction of the symmetry-reduced shortest arc from
        /// the landing angle toward the slot, staying near the landing angle (no full-period jump) and leaving a
        /// (1−gain) residual that is guaranteed within angTol (game-balancer 13 §2.3/§6.2).
        /// </summary>
        private float SettleTargetRotation(float landed, float slot, int symmetryOrder)
        {
            var signed = SignedSymmetryReduce(landed - slot, symmetryOrder);
            var mag = MathF.Abs(signed);
            if (mag <= 1e-6f)
                return landed;

            var maxResidual = _tuning.SettleMaxResidualFactor * _level.MatchTolerance.AngTol;
            var gain = EffectiveGain(mag, maxResidual);
            return landed - signed * gain;
        }

        /// <summary>SettleGain, raised only if the proportional residual (1−gain)·error would exceed the safety
        /// clamp; result is bound to [0, 1]. Higher gain ⇒ smaller residual.</summary>
        private float EffectiveGain(float error, float maxResidual)
        {
            var gain = _tuning.SettleGain;
            var clampGain = 1f - maxResidual / error;
            if (clampGain > gain)
                gain = clampGain;
            if (gain < 0f)
                gain = 0f;
            else if (gain > 1f)
                gain = 1f;
            return gain;
        }

        /// <summary>Easing t∈[0,1]→[0,1]. SmootherStep (default) is C2 at both ends; the others are A/B alts.</summary>
        private static float Ease(SettleEasing curve, float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return 1f;
            switch (curve)
            {
                case SettleEasing.SmoothStep:
                    return t * t * (3f - 2f * t);
                case SettleEasing.EaseOutCubic:
                    var inv = 1f - t;
                    return 1f - inv * inv * inv;
                default: // SmootherStep — 6t⁵−15t⁴+10t³
                    return t * t * t * (t * (t * 6f - 15f) + 10f);
            }
        }

        /// <summary>
        /// Nearest unclaimed slot of the same Kind within the snap capture window (position AND symmetry-reduced
        /// rotation), or -1 when snapping is off or nothing qualifies. Mirrors the evaluator's admissibility so
        /// a snapped piece trivially matches (RFC 07 §2.2).
        /// </summary>
        private int FindSnapSlot(PieceInstance piece, int bodyId)
        {
            if (!_tuning.SnapToSlotEnabled)
                return -1;

            var slots = _level.Target?.Slots;
            if (slots == null)
                return -1;

            var pose = _simulation.GetPose(bodyId);
            var symmetryOrder = _typeByInstance[piece.InstanceId].SymmetryOrder;

            var best = -1;
            var bestPosErr = float.MaxValue;
            for (var s = 0; s < slots.Count; s++)
            {
                if (_occupiedSlots.Contains(s))
                    continue;

                var slot = slots[s];
                if (slot.Kind != piece.Kind)
                    continue;

                var posErr = (slot.Position - pose.Position).Length;
                if (posErr > _tuning.SnapCaptureRadius)
                    continue;

                var angErr = SymmetryReduce(pose.Rotation - slot.Rotation, symmetryOrder);
                if (angErr > _tuning.SnapCaptureAngle)
                    continue;

                if (posErr < bestPosErr)
                {
                    bestPosErr = posErr;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// Reduce an angle delta into the piece's symmetry period and return its magnitude (box order=4 → 90°;
        /// triangle/half order=1 → full circle). Same reduction as <see cref="PoseMatchEvaluator"/> so the snap
        /// window and the win check agree.
        /// </summary>
        private static float SymmetryReduce(float delta, int symmetryOrder)
            => MathF.Abs(SignedSymmetryReduce(delta, symmetryOrder));

        /// <summary>Signed counterpart of <see cref="SymmetryReduce"/>: the reduced delta in (−period/2, period/2].
        /// Used to glide along the shortest arc toward a slot while keeping the sign of the residual.</summary>
        private static float SignedSymmetryReduce(float delta, int symmetryOrder)
        {
            var order = symmetryOrder < 1 ? 1 : symmetryOrder;
            var period = (2f * MathF.PI) / order;
            var d = delta % period;
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return d;
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
            _occupiedSlots.Clear();
            _settling.Clear();
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
