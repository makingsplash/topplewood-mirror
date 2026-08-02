using System;
using System.Collections.Generic;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Core.Mechanic
{
    /// <summary>
    /// Fills the <c>IMechanicController</c> seam with the pivoted shape_fill loop (GAME_RULES §2–3), all pure
    /// C# (headless-testable via ScriptedPoseSimulation): all pieces are pre-placed Idle over their target
    /// slots; a tap hit-tests which Idle piece it hit and, in the immediate-release modes (assembly + catapult),
    /// materialises it into free fall in the SAME step — however many pieces are already falling (TASK-069:
    /// player input is sacrosanct, so a tap never waits; winnability is an AUTHORING property, not an input
    /// limit — this reverts the TASK-061 serialized release queue). Balance keeps its synchronous coalescence
    /// group (§L9, TASK-023) — a symmetric pair still enters the physics together. Releasing materialises the body
    /// so it falls; poses are mirrored into PieceInstances; the Idle→Falling→Placed/Lost FSM runs; and on global
    /// rest a pose-match decides Won/Lost. Physics itself lives behind IShapeFillSimulation.
    ///
    /// TASK-043 (assist-layer removal): the whole pre-physics assist layer is gone — the in-fall snap-to-slot
    /// crystallisation, the soft-settle glide, the hybrid balance load-snap, and the TASK-042 honesty scaffolding
    /// (baseline map, snap-admissibility, win-only two-pass, support/overlap guards, reason counters) that existed
    /// only to gate those assists. A released piece now falls, hits, rotates and comes to rest under honest physics
    /// (variant B), is frozen EXACTLY at its physical rest pose, and the pose-match JUDGES the settled result from
    /// those physical poses (posTol/angTol + the rigidity spread in <see cref="PoseMatchEvaluator"/>). The honesty
    /// detector remains, but sim-local (see <c>SimRun</c>), as a regression instrument.
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
        private LevelData _level;
        // Balance physics mode (TASK-021 → free-beam TASK-023): the beam is a FREE Dynamic body resting on the
        // fulcrum (no world hinge/pin/limits), loads are released as synchronous groups (coalescence window), there
        // is no in-fall snap, and the verdict waits for GLOBAL rest so a still-swinging beam is never judged. Since
        // TASK-043 the balance path freezes every load at its physical rest pose (no slot glide). Assembly mode is
        // the ordinary freeze-on-rest stack.
        private bool _balanceMode;
        // Catapult sub-mode of balance (TASK-025 → de-pinned TASK-027): _balanceMode is ALSO true here, so the whole
        // free-body machinery (no snap, PlaceDynamic for arm+loads, global-rest verdict) runs unchanged. The arm is a
        // FREE Dynamic body on a LOW fulcrum, its tilt limited by the NATURAL ground-stop. _catapultMode adds only ONE
        // delta: coalescence is suppressed so the charge→overload chain is released as sequential SINGLE taps.
        private bool _catapultMode;
        // Time the whole field has stayed below the rest speed, in balance mode only — the verdict fires only
        // once it exceeds RestDurationSeconds (the beam and everything on it have stopped swinging).
        private float _globalRestTimer;

        // ── Synchronous group release (TASK-023, balance-only). Taps on separate Idle pieces are buffered into a
        //    coalescence group and released in ONE flush step so a symmetric pair enters the physics together and
        //    the free beam stays level. Transient runtime state — NOT persisted. Assembly mode never touches it. ──
        // InstanceIds tapped-and-armed, waiting for the flush.
        private readonly List<int> _pendingRelease = new();
        // >0 ⇒ the coalescence window is open (counts down in Tick).
        private int _coalesceWindowStepsLeft;
        // Last computed global-rest state, exposed via ReadyForNextRelease so the sim driver releases the next
        // group only once the beam + loads have settled (balance) — recomputed each tick the field is not falling.
        private bool _lastGlobalRestReached;

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
        }

        private void OnLevelLoaded()
        {
            _level = _model.CurrentLevel;
            _balanceMode = _level.PhysicsMode is PhysicsMode.Balance or PhysicsMode.Catapult;
            _catapultMode = _level.PhysicsMode == PhysicsMode.Catapult;

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
            // A preplaced piece (TASK-031b) instead spawns Dynamic on the field right now — it is a lever/obstacle
            // in the physics from t=0, never a tap target.
            foreach (var piece in _model.Pieces)
            {
                if (_level.Pieces[piece.PlacementIndex].Preplaced)
                    SpawnPreplaced(piece);
                else
                    SpawnIdle(piece);
            }
        }

        public void OnIntent(InputIntent intent)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            var hit = PickIdlePieceAt(new Vec2(intent.X, intent.Y));
            if (hit == null)
                return;

            if (!_balanceMode)
            {
                // Assembly (TASK-069): one tap = release NOW, in this same step, no matter how many pieces are
                // already falling. Player input is never serialized — a fast multi-tap drops every hit piece at
                // once; the level stays winnable by AUTHORING, not by throttling taps. For a rest-gated sim sweep
                // the field is quiet between taps anyway, so this is byte-for-byte the pre-TASK-061 immediate path.
                ReleasePiece(hit);
                return;
            }

            if (_catapultMode)
            {
                // Catapult (TASK-025): the solution is a SEQUENTIAL single-tap chain (fulcrum → arm → pebble charges
                // the far end → boulder overloads the near end and flips the arm). It stays SINGLE-tap (never
                // coalesced into a synchronous group — that would launch pebble+boulder together and break the
                // charge), but like assembly each tap releases IMMEDIATELY (TASK-069): the player owns the timing.
                ReleasePiece(hit);
                return;
            }

            // Balance (TASK-023): buffer the tap into the coalescence group instead of releasing now. The piece
            // stays Idle in the model until the flush (PickIdlePieceAt must keep seeing it while the window is
            // open), and the whole group releases synchronously in FlushPendingRelease so a symmetric pair enters
            // the physics in the same step (net torque ≈ 0 → the free beam holds level).
            if (_pendingRelease.Contains(hit.InstanceId))
                return; // re-tap on an already-armed piece — ignore, not a duplicate

            _pendingRelease.Add(hit.InstanceId);
            if (_coalesceWindowStepsLeft <= 0)
                _coalesceWindowStepsLeft = _tuning.CoalesceWindowSteps; // first tap opens the window
            _viewByInstance[hit.InstanceId].SetArmed(true);

            if (_pendingRelease.Count >= _tuning.MaxCoalesceGroup)
                FlushPendingRelease(); // group full → release now (minimal latency)
        }

        /// <summary>Materialise one Idle piece into free fall (assembly path and every coalescence flush).</summary>
        private void ReleasePiece(PieceInstance piece)
        {
            _simulation.Release(_bodyByInstance[piece.InstanceId]);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Falling);
            _model.RegisterAttempt();
        }

        /// <summary>
        /// Release the whole armed coalescence group in one shot (TASK-023 §2.2): every buffered piece becomes
        /// physical NOW, so the next <c>_simulation.Step</c> integrates them together — a symmetric pair falls
        /// synchronously. Order within the flush is irrelevant (no physics step runs between the Release calls),
        /// which keeps the release deterministic. A group of one (base piece, or a lone tap whose window expired)
        /// releases a single piece → on a seesaw that honestly tips the beam → Lost (the winnability gate).
        /// </summary>
        private void FlushPendingRelease()
        {
            foreach (var instanceId in _pendingRelease)
            {
                var piece = FindPiece(instanceId);
                if (piece == null || piece.State != PieceLifecycle.Idle)
                    continue;
                ReleasePiece(piece);
                _viewByInstance[instanceId].SetArmed(false);
            }
            _pendingRelease.Clear();
            _coalesceWindowStepsLeft = 0;
        }

        private PieceInstance FindPiece(int instanceId)
        {
            foreach (var piece in _model.Pieces)
                if (piece.InstanceId == instanceId)
                    return piece;
            return null;
        }

        /// <summary>TASK-054: the single source of truth for "is this piece a tool that may be lost freely?".
        /// Read from <see cref="LevelData"/> via PlacementIndex — exactly as OnLevelLoaded reads Preplaced — so
        /// PieceInstance stays a clean runtime model (no new save field). Absent flag → false → essential.</summary>
        private bool IsExpendable(PieceInstance piece) => _level.Pieces[piece.PlacementIndex].Expendable;

        public void Tick(float dt)
        {
            if (_model.CurrentPhase != GamePhase.Running)
                return;

            // Balance coalescence window (TASK-023): count it down and flush the armed group when it expires. A
            // window that expires with a single armed piece still releases it (honest Lost — no artificial block).
            if (_balanceMode && _coalesceWindowStepsLeft > 0)
            {
                _coalesceWindowStepsLeft--;
                if (_coalesceWindowStepsLeft == 0)
                    FlushPendingRelease();
            }

            var anyFalling = false;

            foreach (var piece in _model.Pieces)
            {
                // Losing a piece past the bounds resolves an ESSENTIAL one Lost immediately (TASK-054 §3) —
                // both for a Falling piece (inside UpdateFalling) and a Placed one (the check below, TASK-059).
                // Once that fires the phase leaves Running, so the remaining iterations must NOT keep mutating
                // the model: bail out before MirrorPose can drag a resolved model, and before a second
                // simultaneous loss double-Resolves in the same tick.
                if (_model.CurrentPhase != GamePhase.Running)
                    return;

                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                if (piece.State == PieceLifecycle.Falling)
                {
                    UpdateFalling(piece, bodyId, dt);
                    if (piece.State == PieceLifecycle.Falling)
                        anyFalling = true;
                }
                else if (piece.State == PieceLifecycle.Placed && !_simulation.IsWithinBounds(bodyId))
                {
                    // TASK-059: a Placed piece is only ever bounds-checked HERE. In balance/catapult it stays
                    // Dynamic and rides the beam, so a later drop can knock it off the field; without this it
                    // keeps falling forever, GlobalRestReached never fires, and the level hangs on Timeout. Same
                    // verdict as a Falling piece (symmetry): essential → immediate Lost; expendable → silent
                    // leave. In assembly mode a Placed piece is frozen kinematic in-bounds, so this never trips.
                    // LosePastBounds despawns the body, so skip MirrorPose this iteration.
                    LosePastBounds(piece, bodyId);
                    continue;
                }

                // The body may have been despawned (Lost) inside UpdateFalling / the Placed check above.
                if (_bodyByInstance.TryGetValue(piece.InstanceId, out bodyId))
                    MirrorPose(piece, bodyId);
            }

            // Defer the verdict while any piece is still falling.
            if (anyFalling)
                return;

            // Balance mode: placed pieces stay Dynamic and keep swinging on the beam, so "nothing Falling" is NOT
            // yet rest. Hold the verdict until the whole field has been quiet for RestDurationSeconds, then judge
            // the settled pose (beam within angTol of level ⇒ balanced). _lastGlobalRestReached is cached here for
            // ReadyForNextRelease (the sim's next-group gate). Assembly mode is unaffected.
            if (_balanceMode)
            {
                _lastGlobalRestReached = GlobalRestReached(dt);
                if (!_lastGlobalRestReached)
                    return;
            }

            ResolveAtRest();
        }

        /// <summary>
        /// Field-readiness gate for the sim driver's next release group (TASK-023 §5.2) — encapsulates BOTH modes
        /// so the sim never re-derives it. Never ready while anything is Falling. Assembly ≡ the old "no Falling
        /// piece" quiet check (byte-for-byte). Balance additionally waits until the coalescence group has flushed
        /// (nothing armed, window closed) and the whole field has reached global rest — so the next pair is released
        /// only onto a beam that has stopped swinging.
        /// </summary>
        public bool ReadyForNextRelease
        {
            get
            {
                foreach (var piece in _model.Pieces)
                    if (piece.State == PieceLifecycle.Falling)
                        return false;

                if (!_balanceMode)
                    return true; // assembly: identical to the pre-TASK-023 IsFieldQuiet

                if (_pendingRelease.Count > 0 || _coalesceWindowStepsLeft > 0)
                    return false; // a group is still arming
                return _lastGlobalRestReached; // beam + loads have come to global rest
            }
        }

        /// <summary>
        /// True once EVERY live body has stayed below the rest speed for RestDurationSeconds (balance mode only).
        /// A placed-but-Dynamic pivot beam and the loads riding it keep swinging after they leave the Falling
        /// state; judging the figure before they settle would read a mid-crank pose (and a divergent poseHash).
        /// Any body still moving resets the timer, so the gate reflects a genuine single global rest.
        /// </summary>
        private bool GlobalRestReached(float dt)
        {
            foreach (var bodyId in _bodyByInstance.Values)
            {
                if (_simulation.GetSpeed(bodyId) >= _tuning.RestSpeedEpsilon)
                {
                    _globalRestTimer = 0f;
                    return false;
                }
            }

            _globalRestTimer += dt;
            return _globalRestTimer >= _tuning.RestDurationSeconds;
        }

        /// <summary>
        /// A piece has left the field past the bounds — the loss verdict shared by the Falling path (TASK-054 §3)
        /// and the Placed path (TASK-059). Transition to Lost, drop the view/body, and for an ESSENTIAL piece
        /// (part of the target figure) fail the level NOW: the loss is terminal, no later event can bring it back,
        /// so waiting for global rest only risks a hang. An EXPENDABLE piece (a tool) leaves silently and the
        /// verdict is still decided at rest. The caller's Tick loop early-exits once the phase leaves Running, so a
        /// second simultaneous loss in the same tick never double-Resolves. The slot the piece filled un-fills by
        /// itself: CollectPlaced re-reads only live Placed pieces, so a despawned one simply drops out of the match.
        /// </summary>
        private void LosePastBounds(PieceInstance piece, int bodyId)
        {
            piece.State = PieceLifecycle.Lost;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Lost);
            _simulation.Despawn(bodyId);
            _bodyByInstance.Remove(piece.InstanceId);
            if (!IsExpendable(piece))
                _model.Resolve(GameOutcome.Lost);
        }

        private void UpdateFalling(PieceInstance piece, int bodyId, float dt)
        {
            if (!_simulation.IsWithinBounds(bodyId))
            {
                LosePastBounds(piece, bodyId);
                return;
            }

            // TASK-043: no in-fall snap. A released piece falls under honest physics and is placed the moment it
            // has stayed below the rest speed for RestDurationSeconds — frozen EXACTLY where physics left it (the
            // whole visible turn is falling-hitting-rotating-sliding; posTol/angTol only JUDGE the result).
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
        /// Freeze a piece at its physical rest pose (TASK-043, honest physics only). Freezing (kinematic) kills
        /// post-rest drift and makes the piece a rigid support the next drop cannot shove (RFC 07 §2.1). Assembly
        /// pieces reach here after falling and coming to rest on the floor or an already-Placed piece; the pose is
        /// whatever physics produced and the win check judges it from there. Balance-mode pieces return early to
        /// <see cref="PlaceDynamic"/> — they stay Dynamic and keep riding the beam until global rest.
        /// </summary>
        private void Place(PieceInstance piece, int bodyId)
        {
            var pose = _simulation.GetPose(bodyId);

            if (_balanceMode)
            {
                // Free-beam (TASK-023, extended to catapults TASK-027): BOTH the pivot beam/arm and the loads land as
                // free Dynamic bodies judged from their rest pose — nothing is world-pinned. On a seesaw a single load
                // honestly tips the free beam (Lost) while a synchronous symmetric pair keeps it level; on a catapult
                // the free arm rests on a LOW fulcrum with its tilt limited by the natural ground-stop, and the boulder
                // slam flips it around the ridge to launch the pebble. The Pivot flag marks "judged from the free rest
                // pose, never frozen onto a slot".
                PlaceDynamic(piece, pose);
                return;
            }

            FreezePlaced(piece, pose.Position, pose.Rotation);
            _simulation.FreezeAt(bodyId, pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Balance-mode landing for a free-beam body (TASK-023): the beam OR a load piece (pan / weight / cap).
        /// Mark it Placed so it counts toward the verdict, but DO NOT freeze it — it stays Dynamic and rides the
        /// physics as the beam swings, so the figure is only complete once the whole field settles (the global
        /// rest gate in Tick). MirrorPose keeps its model/view pose tracking the live body every tick. Every piece
        /// (beam and loads) is judged from its free rest pose; the loads are frozen in place at global rest by
        /// <see cref="FreezeLoadsAtRest"/>.
        /// </summary>
        private void PlaceDynamic(PieceInstance piece, BodyPose pose)
        {
            FreezePlaced(piece, pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Pure-physics freeze-at-rest for a balance/catapult level (TASK-031d / variant B, TASK-030), simplified in
        /// TASK-043 to drop the removed slot-claim. Called ONLY at the FINAL global rest (no piece Idle, no release
        /// pending — the caller bails on any intermediate rest), so every piece has stayed Dynamic the whole level and
        /// this runs exactly once, on the settled field. For every settled non-pivot piece: FREEZE it EXACTLY at its
        /// physical rest pose — no glide, no position or angle correction. The piece must have reached the silhouette
        /// by real physics (falling, hitting, rotating, launching); posTol/angTol + the rigidity spread only JUDGE the
        /// result. The pivot beam, if any, is left judged from its free rest pose.
        ///
        /// Determinism: freezing only at global rest (never mid-swing) with no physics step between the reads keeps
        /// the pose assignment reproducible, so poseHash stays stable.
        /// </summary>
        private void FreezeLoadsAtRest()
        {
            foreach (var piece in _model.Pieces)
            {
                if (piece.State != PieceLifecycle.Placed)
                    continue;
                if (_typeByInstance[piece.InstanceId].Pivot)
                    continue; // a marked pivot beam is judged from its free rest pose, never frozen onto a slot
                if (!_bodyByInstance.TryGetValue(piece.InstanceId, out var bodyId))
                    continue;

                var pose = _simulation.GetPose(bodyId);
                _simulation.FreezeAt(bodyId, pose.Position, pose.Rotation);
                FreezePlaced(piece, pose.Position, pose.Rotation);
            }
        }

        /// <summary>Freeze a piece Placed at an exact pose and mirror it into the model + view.</summary>
        private void FreezePlaced(PieceInstance piece, Vec2 pos, float rot)
        {
            piece.State = PieceLifecycle.Placed;
            piece.Position = pos;
            piece.Rotation = rot;
            _viewByInstance[piece.InstanceId].SetState(PieceLifecycle.Placed);
            _viewByInstance[piece.InstanceId].Sync(pos, rot);
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
            // TASK-054: only an ESSENTIAL Lost piece blocks the win (an expendable tool may leave the field). After
            // §3 an essential piece never survives Lost to this point — it already resolved the level in UpdateFalling
            // — so anyEssentialLost is defensively false here; the branch keeps the win-gate self-consistent and
            // survives a hypothetical move back to a rest-time verdict. Expendable Lost pieces no longer veto the win.
            var anyEssentialLost = false;
            var anyIdle = false;
            foreach (var piece in _model.Pieces)
            {
                if (piece.State == PieceLifecycle.Lost) { if (!IsExpendable(piece)) anyEssentialLost = true; }
                else if (piece.State == PieceLifecycle.Idle) anyIdle = true;
            }

            // Balance-mode final freeze (TASK-043): every piece stayed Dynamic the whole level. While any piece is
            // still Idle (the player has taps left) OR a coalescence group is arming, the field is not final — bail
            // out without touching poses so a still-launchable load is never frozen mid-turn. Only once nothing is
            // Idle and no release is pending is the field truly final: freeze every non-pivot load EXACTLY where
            // physics parked it, then greedy-match the live poses. This replaces the removed snap/glide crisp — the
            // silhouette must be reached by real physics, so grand_seesaw's slots are authored to the observed rest
            // poses of a level beam (§M).
            if (_balanceMode)
            {
                if (anyIdle || _pendingRelease.Count > 0 || _coalesceWindowStepsLeft > 0)
                    return;
                FreezeLoadsAtRest();
            }

            var result = _match.Evaluate(_level.Target, CollectPlaced(), _level.MatchTolerance);

            if (result.AllSlotsFilled && !anyEssentialLost)
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

        /// <summary>
        /// Spawn a preplaced piece (TASK-031b): identical body/view setup to <see cref="SpawnIdle"/>, but the body
        /// is RELEASED immediately so it drops into the physics at t=0 (Falling), NOT hung Idle. It is therefore
        /// never picked by <see cref="PickIdlePieceAt"/> (Idle-only), never awaited as a tap, and — unlike
        /// <see cref="ReleasePiece"/> — does NOT count a player attempt (it is level furniture, not a move). From
        /// here it follows the ordinary fall→rest→Place path; in balance/catapult it lands as a free Dynamic body
        /// (PlaceDynamic) so a later drop can pivot it.
        /// </summary>
        private void SpawnPreplaced(PieceInstance piece)
        {
            var type = _model.TypeFor(piece);
            var bodyId = _simulation.SpawnFloating(type, piece.Position, piece.Rotation);

            _bodyByInstance[piece.InstanceId] = bodyId;
            _typeByInstance[piece.InstanceId] = type;

            _simulation.Release(bodyId);
            piece.State = PieceLifecycle.Falling;
            piece.RestTimer = 0f;

            var view = _viewFactory.Create(type);
            view.SetState(PieceLifecycle.Falling);
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
            _globalRestTimer = 0f;
            _pendingRelease.Clear();
            _coalesceWindowStepsLeft = 0;
            _lastGlobalRestReached = false;
            // NB: _catapultMode / _balanceMode are deliberately NOT reset here — ClearRuntime runs AFTER
            // OnLevelLoaded has set the mode from the level, so clearing them would clobber it.
        }

        public void Dispose()
        {
            _levelLoadedSub?.Dispose();
            _levelLoadedSub = null;
        }
    }
}
